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
                action.DiscoveredAt = now;
            }
            else
            {
                db.PermissionActions.Add(new PermissionAction
                {
                    Id = Guid.NewGuid(),
                    ActionKey = attribute.Key,
                    DisplayName = attribute.DisplayName,
                    DiscoveredAt = now
                });
            }
        }
        await db.SaveChangesAsync();
    }
}
