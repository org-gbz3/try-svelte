using backend.Data;
using Microsoft.EntityFrameworkCore;

namespace backend.Tenancy;

// 運営者API(TenantsController)とテナント内API(TenantMembersController)で共通の、メンバー一覧とロール割り当て。
// どちらもルートの {tenantId} でテナント文脈が設定されている前提で、クエリフィルターにより対象テナントに限定される。
public static class TenantMembers
{
    public sealed record Member(string UserId, string Email, IReadOnlyList<Guid> RoleIds);

    public enum ReplaceRolesResult { Updated, MemberNotFound, InvalidRole }

    public static async Task<List<Member>> ListAsync(AuthDbContext db, Guid tenantId)
    {
        var memberRoles = await db.TenantMemberRoles.ToListAsync();
        var members = await (
            from membership in db.TenantMemberships
            where membership.TenantId == tenantId
            join user in db.Users on membership.UserId equals user.Id
            orderby user.Email
            select new { user.Id, user.Email }
        ).ToListAsync();
        return members.Select(member => new Member(
            member.Id, member.Email!,
            memberRoles.Where(memberRole => memberRole.UserId == member.Id)
                .Select(memberRole => memberRole.TenantRoleId)
                .ToList())).ToList();
    }

    // 全置き換え。削除と追加を1回の SaveChanges にまとめ、途中の状態を残さない。
    public static async Task<ReplaceRolesResult> ReplaceRolesAsync(
        AuthDbContext db, Guid tenantId, string userId, IEnumerable<Guid> roleIds)
    {
        if (!await db.TenantMemberships.AnyAsync(item => item.TenantId == tenantId && item.UserId == userId))
            return ReplaceRolesResult.MemberNotFound;

        var requested = roleIds.Distinct().ToList();
        // クエリフィルターにより、このテナントのロールだけが数えられる。
        if (await db.TenantRoles.CountAsync(role => requested.Contains(role.Id)) != requested.Count)
            return ReplaceRolesResult.InvalidRole;

        var current = await db.TenantMemberRoles.Where(memberRole => memberRole.UserId == userId).ToListAsync();
        db.TenantMemberRoles.RemoveRange(current.Where(memberRole => !requested.Contains(memberRole.TenantRoleId)));
        foreach (var roleId in requested.Except(current.Select(memberRole => memberRole.TenantRoleId)))
            db.TenantMemberRoles.Add(new TenantMemberRole { TenantId = tenantId, UserId = userId, TenantRoleId = roleId });
        await db.SaveChangesAsync();
        return ReplaceRolesResult.Updated;
    }
}
