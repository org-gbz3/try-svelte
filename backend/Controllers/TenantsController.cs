using System.ComponentModel.DataAnnotations;
using backend.Authorization;
using backend.Data;
using backend.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace backend.Controllers;

// 運営者によるテナントの作成・編集・削除と、既存ユーザーの所属・テナントロールの割り当て(decisions/0010参照)。
// 個別テナントのルートにも {tenantId} を使い、TenantResolutionMiddleware でテナント文脈を設定する。
// これにより運営者APIでもクエリフィルター・保存時の検証がそのテナントに限定して働き、
// 他テナントのロールを取り違えて割り当てる実装ミスを防ぐ。
[ApiController]
[Route("api/admin/tenants")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class TenantsController(AuthDbContext db, UserManager<ApplicationUser> userManager) : ControllerBase
{
    // テナント作成時に自動作成するロール。作成直後のテナントを管理できる人がいない状態を避けるため、
    // 全テナント用権限キーへの Write を付与する(後から追加された権限キーは起動時の PermissionActionSync が補完する)。
    public const string DefaultAdminRoleName = "テナント管理者";

    [PermissionKey("Admin.Tenants", "テナント管理")]
    [RequirePermission("Admin.Tenants", PermissionLevel.Read)]
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var tenants = await db.Tenants
            .OrderBy(item => item.Name)
            .Select(item => new TenantListItemResponse(
                item.Id, item.Name, item.CreatedAt,
                db.TenantMemberships.Count(membership => membership.TenantId == item.Id)))
            .ToListAsync();
        return Ok(tenants);
    }

    [PermissionKey("Admin.Tenants", "テナント管理")]
    [RequirePermission("Admin.Tenants", PermissionLevel.Write)]
    [HttpPost]
    public async Task<IActionResult> Create(TenantRequest request)
    {
        var name = request.Name.Trim();
        if (name.Length == 0) return BadRequest(new { message = "テナント名を入力してください。" });

        var created = new Tenant { Id = Guid.NewGuid(), Name = name, CreatedAt = DateTime.UtcNow };
        var adminRole = new TenantRole { Id = Guid.NewGuid(), TenantId = created.Id, Name = DefaultAdminRoleName, IsDefaultAdmin = true };
        db.Tenants.Add(created);
        db.TenantRoles.Add(adminRole);
        var tenantActionIds = await db.PermissionActions
            .Where(action => action.Scope == PermissionScope.Tenant)
            .Select(action => action.Id)
            .ToListAsync();
        foreach (var actionId in tenantActionIds)
        {
            db.TenantRolePermissions.Add(new TenantRolePermission
            {
                TenantRoleId = adminRole.Id,
                PermissionActionId = actionId,
                Level = PermissionLevel.Write
            });
        }
        // テナントと既定ロールを1回の SaveChanges で保存し、ロールのないテナントが残らないようにする。
        await db.SaveChangesAsync();
        return StatusCode(StatusCodes.Status201Created, new { id = created.Id });
    }

    [PermissionKey("Admin.Tenants", "テナント管理")]
    [RequirePermission("Admin.Tenants", PermissionLevel.Read)]
    [HttpGet("{tenantId:guid}")]
    public async Task<IActionResult> Get(Guid tenantId)
    {
        var found = await db.Tenants.SingleOrDefaultAsync(item => item.Id == tenantId);
        if (found is null) return NotFound();

        var roles = await db.TenantRoles
            .OrderBy(role => role.Name)
            .Select(role => new TenantRoleResponse(role.Id, role.Name))
            .ToListAsync();
        var members = await TenantMembers.ListAsync(db, tenantId);
        return Ok(new TenantDetailResponse(found.Id, found.Name, found.CreatedAt, roles, members));
    }

    [PermissionKey("Admin.Tenants", "テナント管理")]
    [RequirePermission("Admin.Tenants", PermissionLevel.Write)]
    [HttpPut("{tenantId:guid}")]
    public async Task<IActionResult> Rename(Guid tenantId, TenantRequest request)
    {
        var name = request.Name.Trim();
        if (name.Length == 0) return BadRequest(new { message = "テナント名を入力してください。" });
        var found = await db.Tenants.SingleOrDefaultAsync(item => item.Id == tenantId);
        if (found is null) return NotFound();
        found.Name = name;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [PermissionKey("Admin.Tenants", "テナント管理")]
    [RequirePermission("Admin.Tenants", PermissionLevel.Write)]
    [HttpDelete("{tenantId:guid}")]
    public async Task<IActionResult> Delete(Guid tenantId)
    {
        var found = await db.Tenants.SingleOrDefaultAsync(item => item.Id == tenantId);
        if (found is null) return NotFound();
        // TenantMemberRoles → TenantRoles は連鎖削除しない外部キーのため、割り当てを明示的に削除する。
        // 同じ SaveChanges にまとめ、EF Core が割り当て → テナントの順に削除する(残りはDBの連鎖削除)。
        db.TenantMemberRoles.RemoveRange(await db.TenantMemberRoles.ToListAsync());
        db.Tenants.Remove(found);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [PermissionKey("Admin.Tenants", "テナント管理")]
    [RequirePermission("Admin.Tenants", PermissionLevel.Write)]
    [HttpPost("{tenantId:guid}/members")]
    public async Task<IActionResult> AddMember(Guid tenantId, AddMemberRequest request)
    {
        if (!await db.Tenants.AnyAsync(item => item.Id == tenantId)) return NotFound();
        // 運営者向けAPIのため、アカウントの有無を応答で区別してよい(公開APIの列挙対策とは扱いが異なる)。
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null) return BadRequest(new { message = "このメールアドレスのユーザーは見つかりません。" });
        if (await db.TenantMemberships.AnyAsync(membership => membership.TenantId == tenantId && membership.UserId == user.Id))
            return BadRequest(new { message = "このユーザーは既に所属しています。" });

        db.TenantMemberships.Add(new TenantMembership { TenantId = tenantId, UserId = user.Id, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return StatusCode(StatusCodes.Status201Created, new { userId = user.Id });
    }

    [PermissionKey("Admin.Tenants", "テナント管理")]
    [RequirePermission("Admin.Tenants", PermissionLevel.Write)]
    [HttpDelete("{tenantId:guid}/members/{userId}")]
    public async Task<IActionResult> RemoveMember(Guid tenantId, string userId)
    {
        var membership = await db.TenantMemberships
            .SingleOrDefaultAsync(item => item.TenantId == tenantId && item.UserId == userId);
        if (membership is null) return NotFound();
        // テナントロールの割り当ては所属への外部キーで連鎖削除される。
        db.TenantMemberships.Remove(membership);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [PermissionKey("Admin.Tenants", "テナント管理")]
    [RequirePermission("Admin.Tenants", PermissionLevel.Write)]
    [HttpPut("{tenantId:guid}/members/{userId}/roles")]
    public async Task<IActionResult> SetMemberRoles(Guid tenantId, string userId, SetMemberRolesRequest request)
    {
        return await TenantMembers.ReplaceRolesAsync(db, tenantId, userId, request.RoleIds) switch
        {
            TenantMembers.ReplaceRolesResult.MemberNotFound => NotFound(),
            TenantMembers.ReplaceRolesResult.InvalidRole => BadRequest(new { message = "存在しないロールが含まれています。" }),
            _ => NoContent()
        };
    }

    public sealed record TenantRequest([Required, StringLength(256)] string Name);

    public sealed record AddMemberRequest([Required, EmailAddress, StringLength(254)] string Email);

    public sealed record SetMemberRolesRequest([Required] IReadOnlyList<Guid> RoleIds);

    public sealed record TenantListItemResponse(Guid Id, string Name, DateTime CreatedAt, int MemberCount);

    public sealed record TenantRoleResponse(Guid Id, string Name);

    public sealed record TenantDetailResponse(
        Guid Id, string Name, DateTime CreatedAt,
        IReadOnlyList<TenantRoleResponse> Roles, IReadOnlyList<TenantMembers.Member> Members);
}
