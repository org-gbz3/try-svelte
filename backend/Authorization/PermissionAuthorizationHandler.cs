using backend.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace backend.Authorization;

// 権限情報をCookieに一切載せず、リクエストごとにDBを参照して判定する。
// ロール・権限の変更を既存のログインセッションへ即時反映するための設計判断(decisions/0001参照)。
public sealed class PermissionAuthorizationHandler(AuthDbContext db, UserManager<ApplicationUser> users)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true) return;
        var userId = users.GetUserId(context.User);
        if (userId is null) return;

        // 複数ロールを保持できるため、保持する全ロールのうち最も高い権限レベルを実効権限とする。
        var levels = await PermissionQueries.ForUser(db, userId, requirement.ActionKey).ToListAsync();

        if (levels.Count > 0 && levels.Max(permission => permission.Level) >= requirement.Level)
            context.Succeed(requirement);
    }
}
