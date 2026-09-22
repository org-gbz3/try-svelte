using backend.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace backend.Authorization;

// 初回起動時に、最初の管理者アカウントを作成する。
// 「管理者」はAdmin.Roles への Write 権限を持つロールとして定義しているため(decisions/0001参照)、
// 既にそのようなロールを持つユーザーが1人でもいれば何もしない(何度起動しても安全)。
// Admin:Bootstrap:Email / Admin:Bootstrap:Password が未設定の場合も何もしない(ブートストラップ不要という正常な状態)。
//
// ユーザー作成(パスワードポリシー等で失敗しうる)を、ロール・権限の作成より先に行う。
// 逆順だと、パスワードが弱くてユーザー作成に失敗した場合でもロール・権限だけが残ってしまい、
// 次回起動時に「管理者は既に存在する」と誤判定してリトライされなくなる(実際に起きた不具合)。
//
// UserManager/RoleManager は呼び出しごとに自動で SaveChanges するため、複数回呼ぶと
// 何もしなければ別々のトランザクションに分かれる。途中で失敗すると一部だけコミット済みの
// 中途半端な状態が残るため、明示的なトランザクションで全体を1つにまとめる(decisions/0002参照)。
public static class AdminBootstrap
{
    private const string RoleName = "Administrator";

    public static async Task RunAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var provider = scope.ServiceProvider;
        var options = provider.GetRequiredService<IOptions<AdminBootstrapOptions>>().Value;
        if (string.IsNullOrWhiteSpace(options.Email) || string.IsNullOrWhiteSpace(options.Password))
            return;

        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("AdminBootstrap");
        var db = provider.GetRequiredService<AuthDbContext>();

        // 「ロールが存在するか」ではなく「そのロールを持つユーザーが実在するか」で判定する。
        var adminExists = await (
            from userRole in db.UserRoles
            join rolePermission in db.RolePermissions on userRole.RoleId equals rolePermission.RoleId
            join action in db.PermissionActions on rolePermission.PermissionActionId equals action.Id
            where action.ActionKey == "Admin.Roles" && rolePermission.Level == PermissionLevel.Write
            select userRole.UserId
        ).AnyAsync();
        if (adminExists) return;

        var users = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = provider.GetRequiredService<RoleManager<IdentityRole>>();

        await using var transaction = await db.Database.BeginTransactionAsync();

        var user = await users.FindByEmailAsync(options.Email.Trim());
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = options.Email.Trim(),
                Email = options.Email.Trim(),
                EmailConfirmed = true,
                CreatedAt = DateTime.UtcNow
            };
            var createUserResult = await users.CreateAsync(user, options.Password);
            if (!createUserResult.Succeeded)
            {
                logger.LogError("最初の管理者アカウントを作成できませんでした。Admin:Bootstrap:Password がパスワードポリシー" +
                    "(12〜128文字、大文字・小文字・数字・記号を含む)を満たしているか確認してください。理由: {Errors}",
                    string.Join(", ", createUserResult.Errors.Select(error => error.Description)));
                return; // transaction を Commit しないため、ここまでの変更はロールバックされる。
            }
        }

        var role = await roles.FindByNameAsync(RoleName);
        if (role is null)
        {
            role = new IdentityRole(RoleName);
            var createRoleResult = await roles.CreateAsync(role);
            if (!createRoleResult.Succeeded)
            {
                logger.LogError("最初の管理者ロールを作成できませんでした: {Errors}",
                    string.Join(", ", createRoleResult.Errors.Select(error => error.Description)));
                return;
            }
        }
        await GrantWriteAsync(db, role.Id, "Admin.Roles");
        await GrantWriteAsync(db, role.Id, "Admin.UserRoles");

        if (!await users.IsInRoleAsync(user, RoleName))
            await users.AddToRoleAsync(user, RoleName);

        await transaction.CommitAsync();
        logger.LogInformation("最初の管理者アカウント({Email})を準備しました。", user.Email);
    }

    private static async Task GrantWriteAsync(AuthDbContext db, string roleId, string actionKey)
    {
        var actionId = await db.PermissionActions
            .Where(action => action.ActionKey == actionKey)
            .Select(action => action.Id)
            .SingleAsync();
        var existing = await db.RolePermissions
            .SingleOrDefaultAsync(permission => permission.RoleId == roleId && permission.PermissionActionId == actionId);
        if (existing is null)
            db.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionActionId = actionId, Level = PermissionLevel.Write });
        else
            existing.Level = PermissionLevel.Write;
        await db.SaveChangesAsync();
    }
}
