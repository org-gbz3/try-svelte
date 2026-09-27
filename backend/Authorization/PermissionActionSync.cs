using System.Reflection;
using backend.Data;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace backend.Authorization;

// アプリ起動時に、コード上の [PermissionKey] を PermissionActions テーブルへ同期する。
// スキーマ変更ではなくデータ同期のため、マイグレーションの事前適用方針とは別に起動時に実行する。
// コードから削除されたキーは自動削除せず残す(実害がなく、必要になれば管理画面で手動整理する)。
public static class PermissionActionSync
{
    public static async Task RunAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var provider = scope.ServiceProvider;
        var actionDescriptors = provider.GetRequiredService<IActionDescriptorCollectionProvider>();
        var db = provider.GetRequiredService<AuthDbContext>();

        var declared = actionDescriptors.ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>()
            .Select(descriptor => descriptor.MethodInfo.GetCustomAttribute<PermissionKeyAttribute>())
            .OfType<PermissionKeyAttribute>()
            .DistinctBy(attribute => attribute.Key)
            .ToList();

        var existing = await db.PermissionActions.ToDictionaryAsync(action => action.ActionKey);
        var now = DateTimeOffset.UtcNow;
        foreach (var attribute in declared)
        {
            if (existing.TryGetValue(attribute.Key, out var action))
            {
                action.DisplayName = attribute.DisplayName;
                action.Scope = attribute.Scope;
                action.DiscoveredAt = now;
            }
            else
            {
                db.PermissionActions.Add(new PermissionAction
                {
                    Id = Guid.NewGuid(),
                    ActionKey = attribute.Key,
                    DisplayName = attribute.DisplayName,
                    Scope = attribute.Scope,
                    DiscoveredAt = now
                });
            }
        }

        // 既存行は読み込み時に、新規行は Add 時に追跡済みのため、Local で全権限キーを参照できる。
        await GrantAllTenantPermissionsToDefaultRolesAsync(db, db.PermissionActions.Local.ToList());

        // 権限キーの同期と既定ロールへの付与を1回の SaveChanges にまとめ、途中の状態を残さない。
        await db.SaveChangesAsync();
    }

    // 既定ロール「テナント管理者」は全テナント用権限キーへの Write を常に持つ。新しい機能の権限キーが追加されても
    // 既存テナントの管理者が操作できなくならないよう、不足・低下している権限をここで補完する(decisions/0011参照)。
    private static async Task GrantAllTenantPermissionsToDefaultRolesAsync(
        AuthDbContext db, IEnumerable<PermissionAction> actions)
    {
        var tenantActionIds = actions
            .Where(action => action.Scope == PermissionScope.Tenant)
            .Select(action => action.Id)
            .ToList();
        if (tenantActionIds.Count == 0) return;

        // 起動時はテナント文脈がないため、全テナントの既定ロールを対象にフィルターを明示的に解除する。
        var defaultRoleIds = await db.TenantRoles
            .IgnoreQueryFilters([AuthDbContext.TenantFilter])
            .Where(role => role.IsDefaultAdmin)
            .Select(role => role.Id)
            .ToListAsync();
        if (defaultRoleIds.Count == 0) return;

        var current = await db.TenantRolePermissions
            .Where(permission => defaultRoleIds.Contains(permission.TenantRoleId))
            .ToListAsync();
        foreach (var roleId in defaultRoleIds)
        {
            foreach (var actionId in tenantActionIds)
            {
                var permission = current.SingleOrDefault(item => item.TenantRoleId == roleId && item.PermissionActionId == actionId);
                if (permission is null)
                    db.TenantRolePermissions.Add(new TenantRolePermission
                    {
                        TenantRoleId = roleId,
                        PermissionActionId = actionId,
                        Level = PermissionLevel.Write
                    });
                else
                    permission.Level = PermissionLevel.Write;
            }
        }
    }
}
