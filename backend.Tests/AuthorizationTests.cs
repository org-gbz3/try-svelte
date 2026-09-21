using System.Net;
using System.Net.Http.Json;
using backend.Authorization;
using backend.Data;
using backend.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
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

public class AuthorizationTests
{
    private const string Password = "Test-password-123!";

    [Fact(DisplayName = "権限のないロールでは保護されたAPIにアクセスできない")]
    public async Task ProtectedApiRejectsUserWithoutPermission()
    {
        using var factory = new AuthorizationFactory();
        using var client = factory.CreateClient();
        await RegisterAndLogin(client, factory);
        using var response = await client.GetAsync("/api/weatherforecast");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact(DisplayName = "Readレベルのロールを割り当てると保護されたAPIにアクセスできる")]
    public async Task ProtectedApiAcceptsUserWithReadPermission()
    {
        using var factory = new AuthorizationFactory();
        using var client = factory.CreateClient();
        var (_, userId) = await RegisterAndLogin(client, factory);
        await factory.GrantRoleWithPermissionAsync(userId, "viewer", "WeatherForecast.Get", PermissionLevel.Read);
        using var response = await client.GetAsync("/api/weatherforecast");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(DisplayName = "ロールの権限変更は再ログインなしで即時に反映される")]
    public async Task PermissionChangeReflectsImmediately()
    {
        using var factory = new AuthorizationFactory();
        using var client = factory.CreateClient();
        var (_, userId) = await RegisterAndLogin(client, factory);
        var roleId = await factory.GrantRoleWithPermissionAsync(userId, "viewer", "WeatherForecast.Get", PermissionLevel.Read);

        using var before = await client.GetAsync("/api/weatherforecast");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        // 既存のログインセッション(Cookie)はそのままに、DB側のロール権限だけを剥奪する。
        await factory.SetRolePermissionAsync(roleId, "WeatherForecast.Get", PermissionLevel.None);

        using var after = await client.GetAsync("/api/weatherforecast");
        Assert.Equal(HttpStatusCode.Forbidden, after.StatusCode);
    }

    [Fact(DisplayName = "複数ロールを保持する場合は権限レベルの和集合(最大値)が適用される")]
    public async Task EffectivePermissionIsMaxAcrossRoles()
    {
        using var factory = new AuthorizationFactory();
        using var client = factory.CreateClient();
        var (_, userId) = await RegisterAndLogin(client, factory);
        // 一方はNone、もう一方はReadを持つ2つのロールを付与する。
        await factory.GrantRoleWithPermissionAsync(userId, "empty-role", "WeatherForecast.Get", PermissionLevel.None);
        await factory.GrantRoleWithPermissionAsync(userId, "reader-role", "WeatherForecast.Get", PermissionLevel.Read);

        using var response = await client.GetAsync("/api/weatherforecast");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(DisplayName = "Write権限を持つロールはRead要求のエンドポイントも利用できる")]
    public async Task WriteLevelSatisfiesReadRequirement()
    {
        using var factory = new AuthorizationFactory();
        using var client = factory.CreateClient();
        var (_, userId) = await RegisterAndLogin(client, factory);
        await factory.GrantRoleWithPermissionAsync(userId, "editor", "WeatherForecast.Get", PermissionLevel.Write);
        using var response = await client.GetAsync("/api/weatherforecast");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(DisplayName = "Admin.Roles権限を持たないログインユーザーはロール管理APIを利用できない")]
    public async Task RoleManagementRejectsUserWithoutPermission()
    {
        using var factory = new AuthorizationFactory();
        using var client = factory.CreateClient();
        await RegisterAndLogin(client, factory);
        using var response = await client.GetAsync("/api/admin/roles");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact(DisplayName = "Admin.Roles未満のRead権限では、ロールの作成はできない")]
    public async Task RoleManagementWriteRejectsReadOnlyPermission()
    {
        using var factory = new AuthorizationFactory();
        using var client = factory.CreateClient();
        var (_, userId) = await RegisterAndLogin(client, factory);
        await factory.GrantRoleWithPermissionAsync(userId, "role-viewer", "Admin.Roles", PermissionLevel.Read);
        using var response = await Post(client, "/api/admin/roles", new { Name = "new-role" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact(DisplayName = "Admin.Rolesへの書き込み権限を持つ管理者はロールを作成・編集・削除できる")]
    public async Task AdminCanManageRoles()
    {
        using var factory = new AuthorizationFactory();
        using var client = factory.CreateClient();
        var (_, adminId) = await RegisterAndLogin(client, factory);
        await factory.GrantRoleWithPermissionAsync(adminId, "role-admin", "Admin.Roles", PermissionLevel.Write);

        using var create = await Post(client, "/api/admin/roles", new { Name = "editors" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var roleId = await factory.GetRoleIdByNameAsync("editors");
        using var setPermissions = await Put(client, $"/api/admin/roles/{roleId}/permissions", new
        {
            Permissions = new[] { new { ActionKey = "WeatherForecast.Get", Level = PermissionLevel.Write } }
        });
        Assert.Equal(HttpStatusCode.NoContent, setPermissions.StatusCode);

        using var rename = await Put(client, $"/api/admin/roles/{roleId}", new { Name = "editors-renamed" });
        Assert.Equal(HttpStatusCode.NoContent, rename.StatusCode);

        using var delete = await Delete(client, $"/api/admin/roles/{roleId}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
    }

    [Fact(DisplayName = "管理者はユーザーにロールを割り当てられる")]
    public async Task AdminCanAssignRoleToUser()
    {
        using var factory = new AuthorizationFactory();
        using var client = factory.CreateClient();
        var (_, adminId) = await RegisterAndLogin(client, factory);
        await factory.GrantRoleWithPermissionAsync(adminId, "user-role-admin", "Admin.UserRoles", PermissionLevel.Write);
        var roleId = await factory.CreateRoleAsync("viewer-only");
        var targetUserId = await factory.CreateOtherUserAsync("target@example.com");

        using var response = await Put(client, $"/api/admin/users/{targetUserId}/roles", new
        {
            Roles = new[] { "viewer-only" }
        });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(roleId, await factory.GetRoleIdByNameAsync("viewer-only"));
    }

    [Fact(DisplayName = "Admin.UserRoles権限を持たないログインユーザーは自分のロールを変更できない")]
    public async Task NonAdminCannotChangeOwnRoles()
    {
        using var factory = new AuthorizationFactory();
        using var client = factory.CreateClient();
        var (_, userId) = await RegisterAndLogin(client, factory);
        await factory.CreateRoleAsync("self-granted-admin");

        using var response = await Put(client, $"/api/admin/users/{userId}/roles", new
        {
            Roles = new[] { "self-granted-admin" }
        });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact(DisplayName = "起動時にコード上の[PermissionKey]がPermissionActionsへ同期される")]
    public async Task StartupSyncsPermissionActions()
    {
        using var factory = new AuthorizationFactory();
        using var client = factory.CreateClient();
        await client.GetAsync("/api/auth/csrf"); // ホストを起動させる
        var keys = await factory.GetPermissionActionKeysAsync();
        Assert.Contains("WeatherForecast.Get", keys);
        Assert.Contains("Admin.Roles", keys);
        Assert.Contains("Admin.UserRoles", keys);
    }

    private static async Task<(string Email, string UserId)> RegisterAndLogin(HttpClient client, AuthorizationFactory factory)
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
        return (email, mail.UserId);
    }

    private static async Task<HttpResponseMessage> Post(HttpClient client, string url, object? body = null) =>
        await Send(client, HttpMethod.Post, url, body);

    private static async Task<HttpResponseMessage> Put(HttpClient client, string url, object? body = null) =>
        await Send(client, HttpMethod.Put, url, body);

    private static async Task<HttpResponseMessage> Delete(HttpClient client, string url) =>
        await Send(client, HttpMethod.Delete, url);

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

    private sealed class RecordingEmailSender : IConfirmationEmailSender
    {
        public List<(string Email, string UserId, string Token)> Messages { get; } = [];
        public Task SendAsync(string email, string userId, string token)
        {
            Messages.Add((email, userId, token));
            return Task.CompletedTask;
        }
    }

    private sealed class AuthorizationFactory : WebApplicationFactory<Program>
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

        // ロールを作成し、指定したAPIアクションに対する権限を付与したうえでユーザーに割り当てる。戻り値はロールID。
        public async Task<string> GrantRoleWithPermissionAsync(string userId, string roleName, string actionKey, PermissionLevel level)
        {
            var roleId = await CreateRoleAsync(roleName);
            await SetRolePermissionAsync(roleId, actionKey, level);
            using var scope = Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var user = await users.FindByIdAsync(userId) ?? throw new InvalidOperationException("ユーザーが見つかりません。");
            Assert.True((await users.AddToRoleAsync(user, roleName)).Succeeded);
            return roleId;
        }

        public async Task<string> CreateRoleAsync(string roleName)
        {
            using var scope = Services.CreateScope();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var role = new IdentityRole(roleName);
            await roles.CreateAsync(role);
            return role.Id;
        }

        public async Task SetRolePermissionAsync(string roleId, string actionKey, PermissionLevel level)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var actionId = await db.PermissionActions
                .Where(action => action.ActionKey == actionKey)
                .Select(action => action.Id)
                .SingleAsync();
            var existing = await db.RolePermissions
                .SingleOrDefaultAsync(permission => permission.RoleId == roleId && permission.PermissionActionId == actionId);
            if (existing is null)
            {
                if (level == PermissionLevel.None) return;
                db.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionActionId = actionId, Level = level });
            }
            else if (level == PermissionLevel.None)
            {
                db.RolePermissions.Remove(existing);
            }
            else
            {
                existing.Level = level;
            }
            await db.SaveChangesAsync();
        }

        public async Task<string> GetRoleIdByNameAsync(string roleName)
        {
            using var scope = Services.CreateScope();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var role = await roles.FindByNameAsync(roleName) ?? throw new InvalidOperationException("ロールが見つかりません。");
            return role.Id;
        }

        public async Task<string> CreateOtherUserAsync(string email)
        {
            using var scope = Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
            Assert.True((await users.CreateAsync(user, Password)).Succeeded);
            return user.Id;
        }

        public async Task<List<string>> GetPermissionActionKeysAsync()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            return await db.PermissionActions.Select(action => action.ActionKey).ToListAsync();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) connection.Dispose();
        }
    }
}
