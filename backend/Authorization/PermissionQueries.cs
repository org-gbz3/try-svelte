using backend.Data;

namespace backend.Authorization;

public sealed record UserPermissionLevel(string ActionKey, PermissionLevel Level);

// ユーザーが保持する全ロールの実効権限行(アクションキー×レベル)を結合するクエリ。
// PermissionAuthorizationHandler と /api/auth/me の両方がこの結合を使うため、判定ロジックを一箇所にまとめる。
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
        where actionKey == null || action.ActionKey == actionKey
        select new UserPermissionLevel(action.ActionKey, rolePermission.Level);
}
