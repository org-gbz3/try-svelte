using System.Net;
using System.Net.Http.Json;
using backend.Authorization;
using backend.Data;
using backend.Services;
using backend.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Xunit;

namespace backend.Tests;

// テナント権限の判定を、本番コードのテナントAPIが揃う前から検証するためのテスト専用エンドポイント。
[ApiController]
[Route("api/tenants/{tenantId:guid}/test-probe")]
public class TenantProbeController : ControllerBase
{
    public const string ActionKey = "Test.TenantProbe";

    [PermissionKey(ActionKey, "テスト用テナントAPI", PermissionScope.Tenant)]
    [RequireTenantPermission(ActionKey, PermissionLevel.Read)]
    [HttpGet]
    public IActionResult Get() => Ok();
}

public class TenantTests
{
    private const string Password = "Test-password-123!";

    [Fact(DisplayName = "未ログインではテナントAPIに401を返す")]
    public async Task TenantApiRejectsAnonymous()
    {
        using var factory = new TenantFactory();
        using var client = factory.CreateClient();
        var tenantId = await factory.CreateTenantAsync("tenant-a");
        using var response = await client.GetAsync($"/api/tenants/{tenantId}/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "所属していないテナントのAPIには403を返す")]
    public async Task TenantApiRejectsNonMember()
    {
        using var factory = new TenantFactory();
        using var client = factory.CreateClient();
        await RegisterAndLogin(client, factory);
        var tenantId = await factory.CreateTenantAsync("tenant-a");
        using var response = await client.GetAsync($"/api/tenants/{tenantId}/me");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact(DisplayName = "所属メンバーはテナント名と実効権限を取得できる")]
    public async Task MemberCanGetTenantMe()
    {
        using var factory = new TenantFactory();
        using var client = factory.CreateClient();
        var userId = await RegisterAndLogin(client, factory);
        var tenantId = await factory.CreateTenantAsync("tenant-a");
        await factory.AddMemberAsync(tenantId, userId);
        await factory.GrantTenantRoleAsync(tenantId, userId, "viewer", TenantProbeController.ActionKey, PermissionLevel.Read);

        var me = await client.GetFromJsonAsync<TenantMeResponse>($"/api/tenants/{tenantId}/me");
        Assert.Equal("tenant-a", me!.Name);
        Assert.Equal(PermissionLevel.Read, me.Permissions[TenantProbeController.ActionKey]);
    }

    [Fact(DisplayName = "所属していてもテナントロールの権限がなければテナントAPIを利用できない")]
    public async Task MemberWithoutTenantPermissionIsForbidden()
    {
        using var factory = new TenantFactory();
        using var client = factory.CreateClient();
        var userId = await RegisterAndLogin(client, factory);
        var tenantId = await factory.CreateTenantAsync("tenant-a");
        await factory.AddMemberAsync(tenantId, userId);
        using var response = await client.GetAsync($"/api/tenants/{tenantId}/test-probe");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact(DisplayName = "テナントロールの権限があればテナントAPIを利用できる")]
    public async Task MemberWithTenantPermissionIsAllowed()
    {
        using var factory = new TenantFactory();
        using var client = factory.CreateClient();
        var userId = await RegisterAndLogin(client, factory);
        var tenantId = await factory.CreateTenantAsync("tenant-a");
        await factory.AddMemberAsync(tenantId, userId);
        await factory.GrantTenantRoleAsync(tenantId, userId, "viewer", TenantProbeController.ActionKey, PermissionLevel.Read);
        using var response = await client.GetAsync($"/api/tenants/{tenantId}/test-probe");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(DisplayName = "テナントAの権限ではテナントBのAPIを利用できない")]
    public async Task PermissionDoesNotLeakAcrossTenants()
    {
        using var factory = new TenantFactory();
        using var client = factory.CreateClient();
        var userId = await RegisterAndLogin(client, factory);
        var tenantA = await factory.CreateTenantAsync("tenant-a");
        var tenantB = await factory.CreateTenantAsync("tenant-b");
        await factory.AddMemberAsync(tenantA, userId);
        await factory.AddMemberAsync(tenantB, userId);
        await factory.GrantTenantRoleAsync(tenantA, userId, "viewer", TenantProbeController.ActionKey, PermissionLevel.Read);
        using var response = await client.GetAsync($"/api/tenants/{tenantB}/test-probe");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact(DisplayName = "テナントの所属解除は再ログインなしで即時に反映される")]
    public async Task MembershipRemovalReflectsImmediately()
    {
        using var factory = new TenantFactory();
        using var client = factory.CreateClient();
        var userId = await RegisterAndLogin(client, factory);
        var tenantId = await factory.CreateTenantAsync("tenant-a");
        await factory.AddMemberAsync(tenantId, userId);
        await factory.GrantTenantRoleAsync(tenantId, userId, "viewer", TenantProbeController.ActionKey, PermissionLevel.Read);

        using var before = await client.GetAsync($"/api/tenants/{tenantId}/test-probe");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        // 既存のログインセッション(Cookie)はそのままに、DB側の所属だけを解除する。
        await factory.RemoveMemberAsync(tenantId, userId);

        using var after = await client.GetAsync($"/api/tenants/{tenantId}/test-probe");
        Assert.Equal(HttpStatusCode.Forbidden, after.StatusCode);
    }

    [Fact(DisplayName = "システムロールにテナント用の権限キーを設定できない")]
    public async Task SystemRoleRejectsTenantPermissionKey()
    {
        using var factory = new TenantFactory();
        using var client = factory.CreateClient();
        var userId = await RegisterAndLogin(client, factory);
        await factory.GrantSystemRoleAsync(userId, "role-admin", "Admin.Roles", PermissionLevel.Write);
        var roleId = await factory.CreateSystemRoleAsync("editors");

        using var response = await Put(client, $"/api/admin/roles/{roleId}/permissions", new
        {
            Permissions = new[] { new { ActionKey = TenantProbeController.ActionKey, Level = PermissionLevel.Read } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact(DisplayName = "システムロールの権限キー一覧にテナント用の権限キーを含めない")]
    public async Task SystemPermissionCatalogExcludesTenantKeys()
    {
        using var factory = new TenantFactory();
        using var client = factory.CreateClient();
        var userId = await RegisterAndLogin(client, factory);
        await factory.GrantSystemRoleAsync(userId, "role-viewer", "Admin.Roles", PermissionLevel.Read);

        var actions = await client.GetFromJsonAsync<List<PermissionActionResponse>>("/api/admin/roles/permission-actions");
        Assert.Contains(actions!, action => action.ActionKey == "Admin.Roles");
        Assert.DoesNotContain(actions!, action => action.ActionKey == TenantProbeController.ActionKey);
    }

    [Fact(DisplayName = "別のテナントでは同じ名前のテナントロールを作成できる")]
    public async Task SameRoleNameAllowedInDifferentTenants()
    {
        using var factory = new TenantFactory();
        var tenantA = await factory.CreateTenantAsync("tenant-a");
        var tenantB = await factory.CreateTenantAsync("tenant-b");
        await factory.CreateTenantRoleAsync(tenantA, "editors");
        await factory.CreateTenantRoleAsync(tenantB, "editors");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        Assert.Equal(2, await db.TenantRoles.IgnoreQueryFilters([AuthDbContext.TenantFilter]).CountAsync());
    }

    [Fact(DisplayName = "クエリフィルターにより対象テナント以外の行を取得しない")]
    public async Task QueryFilterHidesOtherTenantRows()
    {
        using var factory = new TenantFactory();
        var tenantA = await factory.CreateTenantAsync("tenant-a");
        var tenantB = await factory.CreateTenantAsync("tenant-b");
        await factory.CreateTenantRoleAsync(tenantA, "role-a");
        await factory.CreateTenantRoleAsync(tenantB, "role-b");

        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(tenantA);
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var names = await db.TenantRoles.Select(role => role.Name).ToListAsync();
        Assert.Equal(["role-a"], names);
    }

    [Fact(DisplayName = "テナント文脈がない場合はテナントに属するデータを取得しない")]
    public async Task QueryFilterWithoutTenantReturnsNothing()
    {
        using var factory = new TenantFactory();
        var tenantId = await factory.CreateTenantAsync("tenant-a");
        await factory.CreateTenantRoleAsync(tenantId, "role-a");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        Assert.Empty(await db.TenantRoles.ToListAsync());
    }

    [Fact(DisplayName = "テナント文脈がある場合はTenantId未指定の追加に対象テナントを設定する")]
    public async Task SaveAssignsCurrentTenantId()
    {
        using var factory = new TenantFactory();
        var tenantId = await factory.CreateTenantAsync("tenant-a");

        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var role = new TenantRole { Id = Guid.NewGuid(), Name = "editors" };
        db.TenantRoles.Add(role);
        await db.SaveChangesAsync();
        Assert.Equal(tenantId, role.TenantId);
    }

    [Fact(DisplayName = "対象テナント以外のTenantIdを持つデータは保存できない")]
    public async Task SaveRejectsOtherTenantId()
    {
        using var factory = new TenantFactory();
        var tenantA = await factory.CreateTenantAsync("tenant-a");
        var tenantB = await factory.CreateTenantAsync("tenant-b");

        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(tenantA);
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        db.TenantRoles.Add(new TenantRole { Id = Guid.NewGuid(), TenantId = tenantB, Name = "editors" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact(DisplayName = "テナント文脈がない場合はTenantId未指定のデータを保存できない")]
    public async Task SaveWithoutTenantRequiresTenantId()
    {
        using var factory = new TenantFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        db.TenantRoles.Add(new TenantRole { Id = Guid.NewGuid(), Name = "editors" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact(DisplayName = "別テナントのロールはメンバーに割り当てられない")]
    public async Task MemberRoleRejectsRoleFromOtherTenant()
    {
        using var factory = new TenantFactory();
        using var client = factory.CreateClient();
        var userId = await RegisterAndLogin(client, factory);
        var tenantA = await factory.CreateTenantAsync("tenant-a");
        var tenantB = await factory.CreateTenantAsync("tenant-b");
        await factory.AddMemberAsync(tenantA, userId);
        var roleInB = await factory.CreateTenantRoleAsync(tenantB, "editors");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        db.TenantMemberRoles.Add(new TenantMemberRole { TenantId = tenantA, UserId = userId, TenantRoleId = roleInB });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact(DisplayName = "起動時にテナント用の権限キーをテナントスコープとして同期する")]
    public async Task PermissionSyncStoresTenantScope()
    {
        using var factory = new TenantFactory();
        using var client = factory.CreateClient();
        await client.GetAsync("/api/auth/csrf"); // ホストを起動させる

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var action = await db.PermissionActions.SingleAsync(item => item.ActionKey == TenantProbeController.ActionKey);
        Assert.Equal(PermissionScope.Tenant, action.Scope);
    }

    private static async Task<string> RegisterAndLogin(HttpClient client, TenantFactory factory)
    {
        var email = $"{Guid.NewGuid():N}@example.com";
        var credentials = new Credentials(email, Password);
        using var registration = await Post(client, "/api/auth/register", credentials);
        registration.EnsureSuccessStatusCode();
        var mail = factory.Sender.Messages.Last(message => message.Email == email);
        using var confirmation = await Post(client, "/api/auth/confirm-email", new { mail.UserId, mail.Token });
        confirmation.EnsureSuccessStatusCode();
        using var login = await Post(client, "/api/auth/login", credentials);
        login.EnsureSuccessStatusCode();
        return mail.UserId;
    }

    private static async Task<HttpResponseMessage> Post(HttpClient client, string url, object? body = null) =>
        await Send(client, HttpMethod.Post, url, body);

    private static async Task<HttpResponseMessage> Put(HttpClient client, string url, object? body = null) =>
        await Send(client, HttpMethod.Put, url, body);

    private static async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string url, object? body = null)
    {
        var csrf = await client.GetFromJsonAsync<Csrf>("/api/auth/csrf");
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-CSRF-TOKEN", csrf!.Token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private record Credentials(string Email, string Password);
    private record Csrf(string Token);
    private record TenantMeResponse(Guid Id, string Name, Dictionary<string, PermissionLevel> Permissions);
    private record PermissionActionResponse(string ActionKey, string DisplayName);

    private sealed class RecordingEmailSender : IConfirmationEmailSender
    {
        public List<(string Email, string UserId, string Token)> Messages { get; } = [];
        public Task SendAsync(string email, string userId, string token)
        {
            Messages.Add((email, userId, token));
            return Task.CompletedTask;
        }
        public Task SendPasswordResetAsync(string email, string userId, string token) => Task.CompletedTask;
    }

    private sealed class TenantFactory : WebApplicationFactory<Program>
    {
        public RecordingEmailSender Sender { get; } = new();
        private readonly SqliteConnection connection = new("Data Source=:memory:");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureLogging(logging => logging.AddFilter<ConsoleLoggerProvider>(
                (_, level) => level >= LogLevel.Error));
            builder.ConfigureServices(services =>
            {
                services.AddControllers().AddApplicationPart(typeof(TenantProbeController).Assembly);
                services.RemoveAll<IConfirmationEmailSender>();
                services.AddSingleton<IConfirmationEmailSender>(Sender);
                services.RemoveAll<DbContextOptions<AuthDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<AuthDbContext>>();
                connection.Open();
                services.AddDbContext<AuthDbContext>(options => options.UseSqlite(connection));
                using var provider = services.BuildServiceProvider();
                using var scope = provider.CreateScope();
                scope.ServiceProvider.GetRequiredService<AuthDbContext>().Database.EnsureCreated();
            });
        }

        public async Task<Guid> CreateTenantAsync(string name)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var tenant = new Tenant { Id = Guid.NewGuid(), Name = name, CreatedAt = DateTime.UtcNow };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
            return tenant.Id;
        }

        public async Task AddMemberAsync(Guid tenantId, string userId)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            db.TenantMemberships.Add(new TenantMembership { TenantId = tenantId, UserId = userId, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        public async Task RemoveMemberAsync(Guid tenantId, string userId)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var membership = await db.TenantMemberships.SingleAsync(item => item.TenantId == tenantId && item.UserId == userId);
            db.TenantMemberships.Remove(membership);
            await db.SaveChangesAsync();
        }

        public async Task<Guid> CreateTenantRoleAsync(Guid tenantId, string name)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var role = new TenantRole { Id = Guid.NewGuid(), TenantId = tenantId, Name = name };
            db.TenantRoles.Add(role);
            await db.SaveChangesAsync();
            return role.Id;
        }

        // テナントロールを作成して権限を付与し、所属済みのユーザーに割り当てる。戻り値はテナントロールID。
        public async Task<Guid> GrantTenantRoleAsync(
            Guid tenantId, string userId, string roleName, string actionKey, PermissionLevel level)
        {
            var roleId = await CreateTenantRoleAsync(tenantId, roleName);
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var actionId = await db.PermissionActions
                .Where(action => action.ActionKey == actionKey)
                .Select(action => action.Id)
                .SingleAsync();
            db.TenantRolePermissions.Add(new TenantRolePermission { TenantRoleId = roleId, PermissionActionId = actionId, Level = level });
            db.TenantMemberRoles.Add(new TenantMemberRole { TenantId = tenantId, UserId = userId, TenantRoleId = roleId });
            await db.SaveChangesAsync();
            return roleId;
        }

        public async Task<string> CreateSystemRoleAsync(string roleName)
        {
            using var scope = Services.CreateScope();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var role = new IdentityRole(roleName);
            Assert.True((await roles.CreateAsync(role)).Succeeded);
            return role.Id;
        }

        // システムロールを作成して権限を付与し、ユーザーに割り当てる。
        public async Task GrantSystemRoleAsync(string userId, string roleName, string actionKey, PermissionLevel level)
        {
            var roleId = await CreateSystemRoleAsync(roleName);
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var actionId = await db.PermissionActions
                .Where(action => action.ActionKey == actionKey)
                .Select(action => action.Id)
                .SingleAsync();
            db.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionActionId = actionId, Level = level });
            await db.SaveChangesAsync();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByIdAsync(userId) ?? throw new InvalidOperationException("ユーザーが見つかりません。");
            Assert.True((await users.AddToRoleAsync(user, roleName)).Succeeded);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) connection.Dispose();
        }
    }
}
