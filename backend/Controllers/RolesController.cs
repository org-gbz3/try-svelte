using System.ComponentModel.DataAnnotations;
using backend.Authorization;
using backend.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace backend.Controllers;

// ロールの作成・編集・削除と、ロールごとのAPI権限の設定。
// 「管理者」を特別なロール名として扱わず、Admin.Roles への Write 権限を持つことそのものを
// 管理者の定義とする(非管理者が自分のロールを変更できないことも、この権限チェックだけで担保する)。
[ApiController]
[Route("api/admin/roles")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class RolesController(RoleManager<IdentityRole> roleManager, AuthDbContext db) : ControllerBase
{
    [PermissionKey("Admin.Roles", "ロール管理")]
    [RequirePermission("Admin.Roles", PermissionLevel.Read)]
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var roles = await roleManager.Roles.ToListAsync();
        var permissions = await db.RolePermissions
            .Join(db.PermissionActions, permission => permission.PermissionActionId, action => action.Id,
                (permission, action) => new { permission.RoleId, action.ActionKey, action.DisplayName, permission.Level })
            .ToListAsync();
        var result = roles.Select(role => new RoleResponse(
            role.Id, role.Name!,
            permissions.Where(permission => permission.RoleId == role.Id)
                .Select(permission => new RolePermissionResponseEntry(permission.ActionKey, permission.DisplayName, permission.Level))
                .ToList()));
        return Ok(result);
    }

    [PermissionKey("Admin.Roles", "ロール管理")]
    [RequirePermission("Admin.Roles", PermissionLevel.Read)]
    [HttpGet("permission-actions")]
    public async Task<IActionResult> PermissionActions()
    {
        var actions = await db.PermissionActions
            .OrderBy(action => action.ActionKey)
            .Select(action => new PermissionActionResponse(action.ActionKey, action.DisplayName))
            .ToListAsync();
        return Ok(actions);
    }

    [PermissionKey("Admin.Roles", "ロール管理")]
    [RequirePermission("Admin.Roles", PermissionLevel.Write)]
    [HttpPost]
    public async Task<IActionResult> Create(RoleRequest request)
    {
        var result = await roleManager.CreateAsync(new IdentityRole(request.Name.Trim()));
        if (!result.Succeeded)
            return BadRequest(new { message = "このロール名では作成できません。" });
        return StatusCode(StatusCodes.Status201Created);
    }

    [PermissionKey("Admin.Roles", "ロール管理")]
    [RequirePermission("Admin.Roles", PermissionLevel.Write)]
    [HttpPut("{roleId}")]
    public async Task<IActionResult> Rename(string roleId, RoleRequest request)
    {
        var role = await roleManager.FindByIdAsync(roleId);
        if (role is null) return NotFound();
        role.Name = request.Name.Trim();
        var result = await roleManager.UpdateAsync(role);
        if (!result.Succeeded)
            return BadRequest(new { message = "このロール名では変更できません。" });
        return NoContent();
    }

    [PermissionKey("Admin.Roles", "ロール管理")]
    [RequirePermission("Admin.Roles", PermissionLevel.Write)]
    [HttpDelete("{roleId}")]
    public async Task<IActionResult> Delete(string roleId)
    {
        var role = await roleManager.FindByIdAsync(roleId);
        if (role is null) return NotFound();
        await roleManager.DeleteAsync(role);
        return NoContent();
    }

    [PermissionKey("Admin.Roles", "ロール管理")]
    [RequirePermission("Admin.Roles", PermissionLevel.Write)]
    [HttpPut("{roleId}/permissions")]
    public async Task<IActionResult> SetPermissions(string roleId, SetRolePermissionsRequest request)
    {
        if (await roleManager.FindByIdAsync(roleId) is null) return NotFound();

        var actionIds = await db.PermissionActions
            .ToDictionaryAsync(action => action.ActionKey, action => action.Id);
        if (request.Permissions.Any(entry => !actionIds.ContainsKey(entry.ActionKey)))
            return BadRequest(new { message = "存在しない権限キーが含まれています。" });

        // 差分更新ではなく全置き換えにすることで、渡されなかったキーは意図どおり None(未設定)扱いになる。
        var existing = await db.RolePermissions.Where(permission => permission.RoleId == roleId).ToListAsync();
        db.RolePermissions.RemoveRange(existing);
        foreach (var entry in request.Permissions.Where(entry => entry.Level != PermissionLevel.None))
        {
            db.RolePermissions.Add(new RolePermission
            {
                RoleId = roleId,
                PermissionActionId = actionIds[entry.ActionKey],
                Level = entry.Level
            });
        }
        await db.SaveChangesAsync();
        return NoContent();
    }

    public sealed record RoleRequest([Required, StringLength(256)] string Name);

    public sealed record RolePermissionEntry(
        [Required, StringLength(200)] string ActionKey,
        PermissionLevel Level);

    public sealed record SetRolePermissionsRequest(
        [Required] IReadOnlyList<RolePermissionEntry> Permissions);

    public sealed record RoleResponse(string Id, string Name, IReadOnlyList<RolePermissionResponseEntry> Permissions);

    public sealed record RolePermissionResponseEntry(string ActionKey, string DisplayName, PermissionLevel Level);

    public sealed record PermissionActionResponse(string ActionKey, string DisplayName);
}
