using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace backend.Authorization;

// [RequirePermission] が生成する動的なポリシー名("Permission:{ActionKey}:{Level}")を
// 実行時に PermissionRequirement へ変換する。それ以外のポリシー名は標準の実装に委譲する。
public sealed class PermissionAuthorizationPolicyProvider(IOptions<AuthorizationOptions> options)
    : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider fallback = new(options);

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(RequirePermissionAttribute.PolicyPrefix, StringComparison.Ordinal))
            return fallback.GetPolicyAsync(policyName);

        var remainder = policyName[RequirePermissionAttribute.PolicyPrefix.Length..];
        var separatorIndex = remainder.LastIndexOf(':');
        var actionKey = remainder[..separatorIndex];
        var level = Enum.Parse<Data.PermissionLevel>(remainder[(separatorIndex + 1)..]);

        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(actionKey, level))
            .Build();
        return Task.FromResult<AuthorizationPolicy?>(policy);
    }

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => fallback.GetDefaultPolicyAsync();
    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => fallback.GetFallbackPolicyAsync();
}
