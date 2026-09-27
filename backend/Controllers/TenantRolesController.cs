using backend.Authorization;
using backend.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace backend.Controllers;

// テナント内のロールの作成・編集・削除と、ロールごとのテナント用権限の設定。RolesController のテナント版。
// ルートの {tenantId} でテナント文脈が設定され、TenantRoles はクエリフィルターでこのテナントの行だけになる。
[ApiController]
[Route("api/tenants/{tenantId:guid}/roles")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class TenantRolesController(AuthDbContext db) : ControllerBase
{
    private const string DefaultRoleLockedMessage = "既定のテナント管理者ロールは削除・権限変更できません。";

    [PermissionKey("Tenant.Roles", "テナントロール管理", PermissionScope.Tenant)]
    [RequireTenantPermission("Tenant.Roles", PermissionLevel.Read)]
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var roles = await db.TenantRoles.OrderBy(role => role.Name).ToListAsync();
        var roleIds = roles.Select(role => role.Id).ToList();
        var permissions = await (
            from permission in db.TenantRolePermissions
            where roleIds.Contains(permission.TenantRoleId)
            join action in db.PermissionActions on permission.PermissionActionId equals action.Id
            where action.Scope == PermissionScope.Tenant
            select new { permission.TenantRoleId, action.ActionKey, action.DisplayName, permission.Level }
        ).ToListAsync();
        return Ok(roles.Select(role => new TenantRoleResponse(
            role.Id, role.Name, role.IsDefaultAdmin,
            permissions.Where(permission => permission.TenantRoleId == role.Id)
                .Select(permission => new RolesController.RolePermissionResponseEntry(
                    permission.ActionKey, permission.DisplayName, permission.Level))
                .ToList())));
    }

    [PermissionKey("Tenant.Roles", "テナントロール管理", PermissionScope.Tenant)]
    [RequireTenantPermission("Tenant.Roles", PermissionLevel.Read)]
    [HttpGet("permission-actions")]
    public async Task<IActionResult> PermissionActions()
    {
        var actions = await db.PermissionActions
            .Where(action => action.Scope == PermissionScope.Tenant)
            .OrderBy(action => action.ActionKey)
            .Select(action => new RolesController.PermissionActionResponse(action.ActionKey, action.DisplayName))
            .ToListAsync();
        return Ok(actions);
    }

    [PermissionKey("Tenant.Roles", "テナントロール管理", PermissionScope.Tenant)]
    [RequireTenantPermission("Tenant.Roles", PermissionLevel.Write)]
    [HttpPost]
    public async Task<IActionResult> Create(RolesController.RoleRequest request)
    {
        var name = request.Name.Trim();
        if (name.Length == 0 || await db.TenantRoles.AnyAsync(role => role.Name == name))
            return BadRequest(new { message = "このロール名では作成できません。" });
        var created = new TenantRole { Id = Guid.NewGuid(), Name = name };
        db.TenantRoles.Add(created);
        await db.SaveChangesAsync();
        return StatusCode(StatusCodes.Status201Created, new { id = created.Id });
    }

    [PermissionKey("Tenant.Roles", "テナントロール管理", PermissionScope.Tenant)]
    [RequireTenantPermission("Tenant.Roles", PermissionLevel.Write)]
    [HttpPut("{roleId:guid}")]
    public async Task<IActionResult> Rename(Guid roleId, RolesController.RoleRequest request)
    {
        var role = await db.TenantRoles.SingleOrDefaultAsync(item => item.Id == roleId);
        if (role is null) return NotFound();
        var name = request.Name.Trim();
        if (name.Length == 0 || await db.TenantRoles.AnyAsync(item => item.Name == name && item.Id != roleId))
            return BadRequest(new { message = "このロール名では変更できません。" });
        role.Name = name;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [PermissionKey("Tenant.Roles", "テナントロール管理", PermissionScope.Tenant)]
    [RequireTenantPermission("Tenant.Roles", PermissionLevel.Write)]
    [HttpDelete("{roleId:guid}")]
    public async Task<IActionResult> Delete(Guid roleId)
    {
        var role = await db.TenantRoles.SingleOrDefaultAsync(item => item.Id == roleId);
        if (role is null) return NotFound();
        // 既定ロールを消すと、テナント内でロールを管理できる人がいなくなるおそれがあるため禁止する。
        if (role.IsDefaultAdmin) return BadRequest(new { message = DefaultRoleLockedMessage });
        // 割り当て → ロールの外部キーは連鎖削除しないため、同じ SaveChanges で割り当てを先に削除する。
        db.TenantMemberRoles.RemoveRange(
            await db.TenantMemberRoles.Where(memberRole => memberRole.TenantRoleId == roleId).ToListAsync());
        db.TenantRoles.Remove(role);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [PermissionKey("Tenant.Roles", "テナントロール管理", PermissionScope.Tenant)]
    [RequireTenantPermission("Tenant.Roles", PermissionLevel.Write)]
    [HttpPut("{roleId:guid}/permissions")]
    public async Task<IActionResult> SetPermissions(Guid roleId, RolesController.SetRolePermissionsRequest request)
    {
        var role = await db.TenantRoles.SingleOrDefaultAsync(item => item.Id == roleId);
        if (role is null) return NotFound();
        if (role.IsDefaultAdmin) return BadRequest(new { message = DefaultRoleLockedMessage });

        // システム用の権限キーはテナントロールに設定させない。
        var actionIds = await db.PermissionActions
            .Where(action => action.Scope == PermissionScope.Tenant)
            .ToDictionaryAsync(action => action.ActionKey, action => action.Id);
        if (request.Permissions.Any(entry => !actionIds.ContainsKey(entry.ActionKey)))
            return BadRequest(new { message = "存在しない権限キーが含まれています。" });

        // RolesController と同じく全置き換えにし、渡されなかったキーは None(未設定)扱いにする。
        db.TenantRolePermissions.RemoveRange(
            await db.TenantRolePermissions.Where(permission => permission.TenantRoleId == roleId).ToListAsync());
        foreach (var entry in request.Permissions.Where(entry => entry.Level != PermissionLevel.None))
        {
            db.TenantRolePermissions.Add(new TenantRolePermission
            {
                TenantRoleId = roleId,
                PermissionActionId = actionIds[entry.ActionKey],
                Level = entry.Level
            });
        }
        await db.SaveChangesAsync();
        return NoContent();
    }

    public sealed record TenantRoleResponse(
        Guid Id, string Name, bool IsDefaultAdmin,
        IReadOnlyList<RolesController.RolePermissionResponseEntry> Permissions);
}
