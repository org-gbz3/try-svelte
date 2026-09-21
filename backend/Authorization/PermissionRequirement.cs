using backend.Data;
using Microsoft.AspNetCore.Authorization;

namespace backend.Authorization;

public sealed record PermissionRequirement(string ActionKey, PermissionLevel Level) : IAuthorizationRequirement;
