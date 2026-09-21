using System.Net;
using System.Net.Http.Json;
using backend.Authorization;
using backend.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Xunit;

namespace backend.Tests;

public class AdminBootstrapTests
{
    [Fact(DisplayName = "Admin:Bootstrapが未設定なら管理者アカウントは作成されない")]
    public async Task DoesNothingWhenNotConfigured()
    {
        using var factory = new AdminBootstrapFactory();
        using var client = factory.CreateClient();
        await client.GetAsync("/api/auth/csrf"); // ホストを起動させ、起動時処理を実行させる
        Assert.False(await factory.AdministratorRoleExistsAsync());
    }

    [Fact(DisplayName = "Admin:Bootstrapを設定すると初回起動時に管理者アカウントが作成され、ログインしてロール管理APIを使える")]
    public async Task CreatesAdminAccountWhenConfigured()
    {
        using var factory = new AdminBootstrapFactory("admin@example.com", "Bootstrap-password-123!");
        using var client = factory.CreateClient();

        var csrf = await client.GetFromJsonAsync<Csrf>("/api/auth/csrf");
        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { Email = "admin@example.com", Password = "Bootstrap-password-123!" })
        };
        loginRequest.Headers.Add("X-CSRF-TOKEN", csrf!.Token);
        using var login = await client.SendAsync(loginRequest);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        using var roles = await client.GetAsync("/api/admin/roles");
        Assert.Equal(HttpStatusCode.OK, roles.StatusCode);
    }

    [Fact(DisplayName = "管理者ロールが既に存在する場合、ブートストラップを再実行しても重複作成しない")]
    public async Task BootstrapIsIdempotent()
    {
        using var factory = new AdminBootstrapFactory("admin@example.com", "Bootstrap-password-123!");
        using var client = factory.CreateClient();
        await client.GetAsync("/api/auth/csrf"); // 初回起動時のブートストラップを実行させる

        await factory.RunBootstrapAgainAsync();

        Assert.Equal(1, await factory.CountAdministratorRolesAsync());
    }

    private record Csrf(string Token);

    private sealed class AdminBootstrapFactory(string? email = null, string? password = null) : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureLogging(logging => logging.AddFilter<ConsoleLoggerProvider>(
                (_, level) => level >= LogLevel.Error));
            // 開発機の appsettings.Development.json や User Secrets に実際の Admin:Bootstrap 設定があっても
            // テストが影響されないよう、常に明示的に上書きする(未指定時は空文字列で無効化する)。
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Admin:Bootstrap:Email"] = email ?? "",
                    ["Admin:Bootstrap:Password"] = password ?? ""
                }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<AuthDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<AuthDbContext>>();
                connection.Open();
                services.AddDbContext<AuthDbContext>(options => options.UseSqlite(connection));
                using var provider = services.BuildServiceProvider();
                using var scope = provider.CreateScope();
                scope.ServiceProvider.GetRequiredService<AuthDbContext>().Database.EnsureCreated();
            });
        }

        public async Task<bool> AdministratorRoleExistsAsync() => await CountAdministratorRolesAsync() > 0;

        public async Task<int> CountAdministratorRolesAsync()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            return await db.Roles.CountAsync(role => role.Name == "Administrator");
        }

        public Task RunBootstrapAgainAsync() => AdminBootstrap.RunAsync(Services);

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) connection.Dispose();
        }
    }
}
