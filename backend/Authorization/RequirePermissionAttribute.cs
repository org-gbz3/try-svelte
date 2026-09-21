using backend.Data;
using Microsoft.AspNetCore.Authorization;

namespace backend.Authorization;

// [Authorize] の代わりに使う。ポリシー名に権限キーとレベルを埋め込み、
// PermissionAuthorizationPolicyProvider が実行時に PermissionRequirement へ解決する。
public sealed class RequirePermissionAttribute : AuthorizeAttribute
{
    public const string PolicyPrefix = "Permission:";

    public RequirePermissionAttribute(string actionKey, PermissionLevel level)
    {
        ActionKey = actionKey;
        Level = level;
        Policy = $"{PolicyPrefix}{actionKey}:{level}";
    }

    public string ActionKey { get; }
    public PermissionLevel Level { get; }
}
