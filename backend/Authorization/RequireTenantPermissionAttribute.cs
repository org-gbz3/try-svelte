using backend.Data;
using Microsoft.AspNetCore.Authorization;

namespace backend.Authorization;

// /api/tenants/{tenantId} 配下のアクションに付ける。ルートのテナントで保持するテナントロールの権限で判定する。
public sealed class RequireTenantPermissionAttribute : AuthorizeAttribute
{
    public const string PolicyPrefix = "TenantPermission:";

    public RequireTenantPermissionAttribute(string actionKey, PermissionLevel level)
    {
        ActionKey = actionKey;
        Level = level;
        Policy = $"{PolicyPrefix}{actionKey}:{level}";
    }

    public string ActionKey { get; }
    public PermissionLevel Level { get; }
}

// 権限キーを必要とせず、ルートのテナントへの所属だけを要求する(自分の実効権限の取得など)。
public sealed class RequireTenantMemberAttribute : AuthorizeAttribute
{
    public const string PolicyName = "TenantMember";

    public RequireTenantMemberAttribute() => Policy = PolicyName;
}
