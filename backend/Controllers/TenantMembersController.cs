using System.ComponentModel.DataAnnotations;
using backend.Authorization;
using backend.Data;
using backend.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace backend.Controllers;

// テナント内でのメンバー一覧とテナントロールの割り当て。メンバーの追加・所属解除は運営者のみが行う
// (TenantsController、decisions/0010参照)ため、ここでは扱わない。
[ApiController]
[Route("api/tenants/{tenantId:guid}/members")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class TenantMembersController(AuthDbContext db) : ControllerBase
{
    [PermissionKey("Tenant.MemberRoles", "メンバーへのテナントロール割り当て", PermissionScope.Tenant)]
    [RequireTenantPermission("Tenant.MemberRoles", PermissionLevel.Read)]
    [HttpGet]
    public async Task<IActionResult> List(Guid tenantId)
    {
        // 割り当て画面でロール名を表示するため、Tenant.Roles の権限がなくても割り当て先のロール名は返す。
        var roles = await db.TenantRoles
            .OrderBy(role => role.Name)
            .Select(role => new TenantRoleSummary(role.Id, role.Name))
            .ToListAsync();
        return Ok(new TenantMembersResponse(roles, await TenantMembers.ListAsync(db, tenantId)));
    }

    [PermissionKey("Tenant.MemberRoles", "メンバーへのテナントロール割り当て", PermissionScope.Tenant)]
    [RequireTenantPermission("Tenant.MemberRoles", PermissionLevel.Write)]
    [HttpPut("{userId}/roles")]
    public async Task<IActionResult> SetRoles(Guid tenantId, string userId, SetMemberRolesRequest request) =>
        await TenantMembers.ReplaceRolesAsync(db, tenantId, userId, request.RoleIds) switch
        {
            TenantMembers.ReplaceRolesResult.MemberNotFound => NotFound(),
            TenantMembers.ReplaceRolesResult.InvalidRole => BadRequest(new { message = "存在しないロールが含まれています。" }),
            _ => NoContent()
        };

    public sealed record SetMemberRolesRequest([Required] IReadOnlyList<Guid> RoleIds);

    public sealed record TenantRoleSummary(Guid Id, string Name);

    public sealed record TenantMembersResponse(IReadOnlyList<TenantRoleSummary> Roles, IReadOnlyList<TenantMembers.Member> Members);
}
