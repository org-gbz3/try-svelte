using backend.Data;
using backend.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace backend.Authorization;

// PermissionAuthorizationHandler と同じく、所属・テナントロールをCookieに載せずリクエストごとにDBで判定する。
// 所属解除やテナントロールの変更を既存のログインセッションへ即時反映するため(decisions/0010参照)。
public sealed class TenantPermissionAuthorizationHandler(
    AuthDbContext db, UserManager<ApplicationUser> users, TenantContext tenant)
    : AuthorizationHandler<TenantPermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, TenantPermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true) return;
        var userId = users.GetUserId(context.User);
        if (userId is null || tenant.TenantId is not { } tenantId) return;

        if (requirement.ActionKey is null)
        {
            if (await db.TenantMemberships.AnyAsync(membership =>
                    membership.TenantId == tenantId && membership.UserId == userId))
                context.Succeed(requirement);
            return;
        }

        var levels = await PermissionQueries.ForTenantUser(db, tenantId, userId, requirement.ActionKey).ToListAsync();
        if (levels.Count > 0 && levels.Max(permission => permission.Level) >= requirement.Level)
            context.Succeed(requirement);
    }
}
