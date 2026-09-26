using backend.Authorization;
using backend.Data;
using backend.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace backend.Controllers;

// 所属テナント内での自分の情報。テナント画面のナビ表示を権限で出し分けるために使う(/api/auth/me のテナント版)。
[ApiController]
[Route("api/tenants/{tenantId:guid}")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class TenantController(AuthDbContext db, UserManager<ApplicationUser> users, TenantContext tenant)
    : ControllerBase
{
    [RequireTenantMember]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var tenantId = tenant.TenantId!.Value;
        var name = await db.Tenants.Where(item => item.Id == tenantId).Select(item => item.Name).SingleAsync();
        var levels = await PermissionQueries.ForTenantUser(db, tenantId, users.GetUserId(User)!).ToListAsync();
        var permissions = levels
            .GroupBy(permission => permission.ActionKey)
            .ToDictionary(group => group.Key, group => group.Max(permission => permission.Level));
        return Ok(new { id = tenantId, name, permissions });
    }
}
