using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace backend.Authorization;

// [RequirePermission]・[RequireTenantPermission]・[RequireTenantMember] が生成する動的なポリシー名を
// 実行時に各要件へ変換する。それ以外のポリシー名は標準の実装に委譲する。
public sealed class PermissionAuthorizationPolicyProvider(IOptions<AuthorizationOptions> options)
    : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider fallback = new(options);

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName == RequireTenantMemberAttribute.PolicyName)
            return Build(new TenantPermissionRequirement(null, Data.PermissionLevel.None));

        if (policyName.StartsWith(RequireTenantPermissionAttribute.PolicyPrefix, StringComparison.Ordinal))
        {
            var (tenantActionKey, tenantLevel) = Parse(policyName[RequireTenantPermissionAttribute.PolicyPrefix.Length..]);
            return Build(new TenantPermissionRequirement(tenantActionKey, tenantLevel));
        }

        if (!policyName.StartsWith(RequirePermissionAttribute.PolicyPrefix, StringComparison.Ordinal))
            return fallback.GetPolicyAsync(policyName);

        var (actionKey, level) = Parse(policyName[RequirePermissionAttribute.PolicyPrefix.Length..]);
        return Build(new PermissionRequirement(actionKey, level));
    }

    private static (string ActionKey, Data.PermissionLevel Level) Parse(string remainder)
    {
        var separatorIndex = remainder.LastIndexOf(':');
        return (remainder[..separatorIndex], Enum.Parse<Data.PermissionLevel>(remainder[(separatorIndex + 1)..]));
    }

    private static Task<AuthorizationPolicy?> Build(IAuthorizationRequirement requirement) =>
        Task.FromResult<AuthorizationPolicy?>(new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(requirement)
            .Build());

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => fallback.GetDefaultPolicyAsync();
    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => fallback.GetFallbackPolicyAsync();
}
