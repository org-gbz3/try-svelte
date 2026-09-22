using System.ComponentModel.DataAnnotations;
using backend.Authorization;
using backend.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers;

// ユーザーへのロール割り当て。Admin.UserRoles への Write 権限を持つユーザーだけが変更できるため、
// 一般ユーザーは自分自身に対してもこのAPIを呼び出せない。
[ApiController]
[Route("api/admin/users/{userId}/roles")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class UserRolesController(UserManager<ApplicationUser> userManager, AuthDbContext db) : ControllerBase
{
    [PermissionKey("Admin.UserRoles", "ユーザーへのロール割り当て")]
    [RequirePermission("Admin.UserRoles", PermissionLevel.Read)]
    [HttpGet]
    public async Task<IActionResult> Get(string userId)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null) return NotFound();
        var roles = await userManager.GetRolesAsync(user);
        return Ok(new UserRolesResponse(user.Id, roles.ToList()));
    }

    [PermissionKey("Admin.UserRoles", "ユーザーへのロール割り当て")]
    [RequirePermission("Admin.UserRoles", PermissionLevel.Write)]
    [HttpPut]
    public async Task<IActionResult> Set(string userId, SetUserRolesRequest request)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null) return NotFound();

        var requested = request.Roles.Distinct().ToList();
        var current = await userManager.GetRolesAsync(user);

        var toRemove = current.Except(requested).ToList();
        var toAdd = requested.Except(current).ToList();
        if (toRemove.Count == 0 && toAdd.Count == 0) return NoContent();

        // RemoveFromRolesAsync/AddToRolesAsync はそれぞれ内部で個別に SaveChanges するため、
        // 明示的なトランザクションで包み、片方だけ成功してロールが中途半端な状態になるのを防ぐ(decisions/0002参照)。
        await using var transaction = await db.Database.BeginTransactionAsync();

        if (toRemove.Count > 0)
        {
            var removeResult = await userManager.RemoveFromRolesAsync(user, toRemove);
            if (!removeResult.Succeeded)
                return BadRequest(new { message = "ロールの割り当てを変更できません。" });
        }
        if (toAdd.Count > 0)
        {
            var addResult = await userManager.AddToRolesAsync(user, toAdd);
            if (!addResult.Succeeded)
                return BadRequest(new { message = "存在しないロールが含まれています。" });
        }

        await transaction.CommitAsync();
        return NoContent();
    }

    public sealed record UserRolesResponse(string UserId, IReadOnlyList<string> Roles);

    public sealed record SetUserRolesRequest([Required] IReadOnlyList<string> Roles);
}
