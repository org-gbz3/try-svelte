using backend.Data;
using Microsoft.AspNetCore.Authorization;

namespace backend.Authorization;

// ActionKey が null の場合はテナントへの所属だけを要求する。
public sealed record TenantPermissionRequirement(string? ActionKey, PermissionLevel Level) : IAuthorizationRequirement;
