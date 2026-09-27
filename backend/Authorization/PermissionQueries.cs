using backend.Data;

namespace backend.Authorization;

public sealed record UserPermissionLevel(string ActionKey, PermissionLevel Level);

// ユーザーが保持する全ロールの実効権限行(アクションキー×レベル)を結合するクエリ。
// 認可ハンドラーと /me の両方がこの結合を使うため、判定ロジックを一箇所にまとめる。
public static class PermissionQueries
{
    // actionKey を指定すると、DB側でその値に絞り込んでから結合する(単一アクションの判定用)。
    // 省略すると、ユーザーが保持する全ロールの実効権限行を返す(/me の一覧用)。
    // EF Core は Select で生成した record に対する後段の Where 合成を翻訳できないため、
    // フィルタは呼び出し側で合成せずこのクエリ式自身に含める。
    public static IQueryable<UserPermissionLevel> ForUser(AuthDbContext db, string userId, string? actionKey = null) =>
        from userRole in db.UserRoles
        where userRole.UserId == userId
        join rolePermission in db.RolePermissions on userRole.RoleId equals rolePermission.RoleId
        join action in db.PermissionActions on rolePermission.PermissionActionId equals action.Id
        // システムロールにテナント用の権限キーが残っていても、システム権限として扱わない。
        where action.Scope == PermissionScope.System
        where actionKey == null || action.ActionKey == actionKey
        select new UserPermissionLevel(action.ActionKey, rolePermission.Level);

    // 指定テナントでユーザーが保持する全テナントロールの実効権限行。割り当ては所属への外部キーを持つため、
    // 行が返ること自体がそのテナントへの所属を意味する。
    public static IQueryable<UserPermissionLevel> ForTenantUser(
        AuthDbContext db, Guid tenantId, string userId, string? actionKey = null) =>
        from memberRole in db.TenantMemberRoles
        where memberRole.TenantId == tenantId && memberRole.UserId == userId
        join rolePermission in db.TenantRolePermissions on memberRole.TenantRoleId equals rolePermission.TenantRoleId
        join action in db.PermissionActions on rolePermission.PermissionActionId equals action.Id
        where action.Scope == PermissionScope.Tenant
        where actionKey == null || action.ActionKey == actionKey
        select new UserPermissionLevel(action.ActionKey, rolePermission.Level);
}
