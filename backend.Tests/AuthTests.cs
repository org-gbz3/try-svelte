using System.Net;
using System.Net.Http.Json;
using backend.Data;
using Microsoft.AspNetCore.Hosting;
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

public class AuthTests
{
    private const string Password = "Test-password-123!";

    [Fact(DisplayName = "未ログインではユーザー情報を取得できない")]
    public async Task MeRejectsAnonymousUser()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "未ログインでは保護されたAPIにアクセスできない")]
    public async Task ProtectedApiRejectsAnonymousUser()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/weatherforecast");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "登録時にCSRFトークンがなければ拒否する")]
    public async Task RegistrationRequiresCsrfToken()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/auth/register", CreateCredentials());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact(DisplayName = "有効なメールアドレスとパスワードでアカウントを登録できる")]
    public async Task RegistrationCreatesAccount()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        using var response = await Post(client, "register", CreateCredentials());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact(DisplayName = "登録だけではログイン状態にならない")]
    public async Task RegistrationDoesNotSignIn()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        await Register(client);
        using var response = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "登録済みのメールアドレスでの重複登録を拒否する")]
    public async Task RegistrationRejectsDuplicateEmail()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        var credentials = await Register(client);
        using var response = await Post(client, "register", credentials);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact(DisplayName = "不正なメールアドレスでの登録を拒否する")]
    public async Task RegistrationRejectsInvalidEmail()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        using var response = await Post(client, "register", CreateCredentials() with { Email = "invalid" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact(DisplayName = "弱いパスワードでの登録を拒否する")]
    public async Task RegistrationRejectsWeakPassword()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        using var response = await Post(client, "register", CreateCredentials() with { Password = "short" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact(DisplayName = "誤ったパスワードでのログインを拒否する")]
    public async Task LoginRejectsWrongPassword()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        var credentials = await Register(client);
        using var response = await Post(client, "login", credentials with { Password = "wrong" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "正しいパスワードでログインできる")]
    public async Task LoginAcceptsCorrectPassword()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        var credentials = await Register(client);
        using var response = await Post(client, "login", credentials);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(DisplayName = "ログイン時の認証CookieにHttpOnlyを設定する")]
    public async Task LoginCookieUsesHttpOnly()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        var credentials = await Register(client);
        using var response = await Post(client, "login", credentials);
        response.EnsureSuccessStatusCode();
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), cookie =>
            cookie.StartsWith("try-svelte.auth=") && cookie.Contains("httponly"));
    }

    [Fact(DisplayName = "ログイン時の認証CookieにSameSite=Laxを設定する")]
    public async Task LoginCookieUsesSameSiteLax()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        var credentials = await Register(client);
        using var response = await Post(client, "login", credentials);
        response.EnsureSuccessStatusCode();
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), cookie =>
            cookie.StartsWith("try-svelte.auth=") && cookie.Contains("samesite=lax"));
    }

    [Fact(DisplayName = "ログイン後は認証Cookieで本人のユーザー情報を取得できる")]
    public async Task MeReturnsAuthenticatedUser()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        var credentials = await RegisterAndLogin(client);
        using var response = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(credentials.Email, (await response.Content.ReadFromJsonAsync<User>())!.Email);
    }

    [Fact(DisplayName = "ユーザー情報の応答はキャッシュ保存を禁止する")]
    public async Task MeDisablesCaching()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        await RegisterAndLogin(client);
        using var response = await client.GetAsync("/api/auth/me");
        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact(DisplayName = "ログイン後は保護されたAPIにアクセスできる")]
    public async Task ProtectedApiAcceptsAuthenticatedUser()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        await RegisterAndLogin(client);
        using var response = await client.GetAsync("/api/weatherforecast");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(DisplayName = "ログアウト時にCSRFトークンがなければ拒否する")]
    public async Task LogoutRequiresCsrfToken()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        await RegisterAndLogin(client);
        using var response = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact(DisplayName = "ログイン後に再取得したCSRFトークンでログアウトできる")]
    public async Task LogoutAcceptsFreshCsrfToken()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        await RegisterAndLogin(client);
        using var response = await Post(client, "logout");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact(DisplayName = "ログアウト後はユーザー情報を取得できない")]
    public async Task MeRejectsLoggedOutUser()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        await RegisterAndLogin(client);
        using var logout = await Post(client, "logout");
        logout.EnsureSuccessStatusCode();
        using var response = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "ログアウト後は保護されたAPIにアクセスできない")]
    public async Task ProtectedApiRejectsLoggedOutUser()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        await RegisterAndLogin(client);
        using var logout = await Post(client, "logout");
        logout.EnsureSuccessStatusCode();
        using var response = await client.GetAsync("/api/weatherforecast");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "存在しないAPIは404を返す")]
    public async Task UnknownApiReturnsNotFound()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact(DisplayName = "拡張子付きの存在しないAPIも404を返す")]
    public async Task UnknownApiFileReturnsNotFound()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/missing.json");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact(DisplayName = "ログイン失敗5回後は正しいパスワードでもログインを拒否する")]
    public async Task LoginLocksOutAfterFiveFailures()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        var credentials = await Register(client);
        for (var i = 0; i < 5; i++)
        {
            using var failure = await Post(client, "login", credentials with { Password = "wrong" });
            Assert.Equal(HttpStatusCode.Unauthorized, failure.StatusCode);
        }
        using var response = await Post(client, "login", credentials);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "別ブラウザーのCSRFトークンでのログインを拒否する")]
    public async Task LoginRejectsCsrfTokenFromAnotherBrowser()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        var credentials = await Register(client);
        using var other = factory.CreateClient();
        var token = (await other.GetFromJsonAsync<Csrf>("/api/auth/csrf"))!.Token;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(credentials)
        };
        request.Headers.Add("X-CSRF-TOKEN", token);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact(DisplayName = "ログイン前の古いCSRFトークンでのログアウトを拒否する")]
    public async Task LogoutRejectsCsrfTokenIssuedBeforeLogin()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        var token = (await client.GetFromJsonAsync<Csrf>("/api/auth/csrf"))!.Token;
        await RegisterAndLogin(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        request.Headers.Add("X-CSRF-TOKEN", token);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static Credentials CreateCredentials() => new("user@example.com", Password);

    // 前提条件の失敗を検証対象の失敗と混同しないよう、準備時にも応答を確認する。
    private static async Task<Credentials> Register(HttpClient client)
    {
        var credentials = CreateCredentials();
        using var response = await Post(client, "register", credentials);
        response.EnsureSuccessStatusCode();
        return credentials;
    }

    private static async Task<Credentials> RegisterAndLogin(HttpClient client)
    {
        var credentials = await Register(client);
        using var response = await Post(client, "login", credentials);
        response.EnsureSuccessStatusCode();
        return credentials;
    }

    private static async Task<HttpResponseMessage> Post(HttpClient client, string endpoint, object? body = null)
    {
        var csrf = await client.GetFromJsonAsync<Csrf>("/api/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/auth/{endpoint}");
        request.Headers.Add("X-CSRF-TOKEN", csrf!.Token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private record Credentials(string Email, string Password);
    private record Csrf(string Token);
    private record User(string Id, string Email);

    private sealed class AuthFactory : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            // SQL や正常系のログでテストの確認内容が埋もれないようにする。
            builder.ConfigureLogging(logging => logging.AddFilter<ConsoleLoggerProvider>(
                (_, level) => level >= LogLevel.Error));
            builder.ConfigureServices(services =>
            {
                // 外部 DB に依存せず、Identity と Cookie の実際の処理を検証する。
                services.RemoveAll<DbContextOptions<AuthDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<AuthDbContext>>();
                connection.Open();
                services.AddDbContext<AuthDbContext>(options => options.UseSqlite(connection));
                using var provider = services.BuildServiceProvider();
                using var scope = provider.CreateScope();
                scope.ServiceProvider.GetRequiredService<AuthDbContext>().Database.EnsureCreated();
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) connection.Dispose();
        }
    }
}
