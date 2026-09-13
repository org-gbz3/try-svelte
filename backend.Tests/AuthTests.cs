using System.Net;
using System.Net.Http.Json;
using backend.Data;
using backend.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Xunit;

namespace backend.Tests;

public class AuthTests
{
    private const string Password = "Test-password-123!";

    [Theory(DisplayName = "設定した確認メールの有効期限をIdentityに適用する")]
    [InlineData(30)]
    [InlineData(1440)]
    public void ConfirmationLifespanUsesConfiguration(int minutes)
    {
        using var factory = new AuthFactory();
        using var configured = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Email:ConfirmationTokenLifespanMinutes"] = minutes.ToString()
                })));
        using var client = configured.CreateClient();
        Assert.Equal(TimeSpan.FromMinutes(minutes), configured.Services
            .GetRequiredService<IOptions<DataProtectionTokenProviderOptions>>().Value.TokenLifespan);
        Assert.Equal(minutes, configured.Services
            .GetRequiredService<IOptions<EmailOptions>>().Value.ConfirmationTokenLifespanMinutes);
    }

    [Theory(DisplayName = "確認メールの有効期限が0以下なら起動を拒否する")]
    [InlineData(0)]
    [InlineData(-1)]
    public void ConfirmationLifespanRejectsInvalidConfiguration(int minutes)
    {
        using var factory = new AuthFactory();
        using var configured = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Email:ConfirmationTokenLifespanMinutes"] = minutes.ToString()
                })));
        Assert.Throws<OptionsValidationException>(() => configured.CreateClient());
    }

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
        await Register(client, factory);
        using var response = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "登録済みのメールアドレスでの重複登録を拒否する")]
    public async Task RegistrationRejectsDuplicateEmail()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        var credentials = await Register(client, factory);
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
        var credentials = await Register(client, factory);
        using var response = await Post(client, "login", credentials with { Password = "wrong" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "正しいパスワードでログインできる")]
    public async Task LoginAcceptsCorrectPassword()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        var credentials = await Register(client, factory);
        using var response = await Post(client, "login", credentials);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(DisplayName = "ログイン時の認証CookieにHttpOnlyを設定する")]
    public async Task LoginCookieUsesHttpOnly()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        var credentials = await Register(client, factory);
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
        var credentials = await Register(client, factory);
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
        var credentials = await RegisterAndLogin(client, factory);
        using var response = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(credentials.Email, (await response.Content.ReadFromJsonAsync<User>())!.Email);
    }

    [Fact(DisplayName = "ユーザー情報の応答はキャッシュ保存を禁止する")]
    public async Task MeDisablesCaching()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        await RegisterAndLogin(client, factory);
        using var response = await client.GetAsync("/api/auth/me");
        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact(DisplayName = "ログイン後は保護されたAPIにアクセスできる")]
    public async Task ProtectedApiAcceptsAuthenticatedUser()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        await RegisterAndLogin(client, factory);
        using var response = await client.GetAsync("/api/weatherforecast");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(DisplayName = "ログアウト時にCSRFトークンがなければ拒否する")]
    public async Task LogoutRequiresCsrfToken()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        await RegisterAndLogin(client, factory);
        using var response = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact(DisplayName = "ログイン後に再取得したCSRFトークンでログアウトできる")]
    public async Task LogoutAcceptsFreshCsrfToken()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        await RegisterAndLogin(client, factory);
        using var response = await Post(client, "logout");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact(DisplayName = "ログアウト後はユーザー情報を取得できない")]
    public async Task MeRejectsLoggedOutUser()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        await RegisterAndLogin(client, factory);
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
        await RegisterAndLogin(client, factory);
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
        var credentials = await Register(client, factory);
        for (var i = 0; i < 5; i++)
        {
            using var failure = await Post(client, "login", credentials with { Password = "wrong" });
            Assert.Equal(HttpStatusCode.Unauthorized, failure.StatusCode);
        }
        using var response = await Post(client, "login", credentials);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "ログインへの過剰なリクエストはレート制限で拒否する")]
    public async Task LoginEnforcesRateLimit()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        var credentials = CreateCredentials();
        HttpStatusCode lastStatus = HttpStatusCode.OK;
        for (var i = 0; i < 21; i++)
        {
            using var response = await Post(client, "login", credentials);
            lastStatus = response.StatusCode;
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, lastStatus);
    }

    [Fact(DisplayName = "別ブラウザーのCSRFトークンでのログインを拒否する")]
    public async Task LoginRejectsCsrfTokenFromAnotherBrowser()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        var credentials = await Register(client, factory);
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
        await RegisterAndLogin(client, factory);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        request.Headers.Add("X-CSRF-TOKEN", token);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact(DisplayName = "メール未確認では正しいパスワードでもログインできない")]
    public async Task LoginRejectsUnconfirmedEmail()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        using var registration = await Post(client, "register", CreateCredentials());
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        Assert.Equal(CreateCredentials().Email, Assert.Single(factory.Sender.Messages).Email);
        using var response = await Post(client, "login", CreateCredentials());
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "改ざんされた確認トークンを拒否する")]
    public async Task ConfirmationRejectsInvalidToken()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        using var registration = await Post(client, "register", CreateCredentials());
        registration.EnsureSuccessStatusCode();
        var mail = Assert.Single(factory.Sender.Messages);
        using var response = await Post(client, "confirm-email", new { mail.UserId, Token = mail.Token + "invalid" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact(DisplayName = "期限切れの確認トークンを拒否する")]
    public async Task ConfirmationRejectsExpiredToken()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        factory.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Identity.DataProtectionTokenProviderOptions>>()
            .Value.TokenLifespan = TimeSpan.FromSeconds(-1);
        using var registration = await Post(client, "register", CreateCredentials());
        registration.EnsureSuccessStatusCode();
        var mail = Assert.Single(factory.Sender.Messages);
        using var response = await Post(client, "confirm-email", new { mail.UserId, mail.Token });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact(DisplayName = "メール確認にもCSRFトークンを要求する")]
    public async Task ConfirmationRequiresCsrf()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        using var registration = await Post(client, "register", CreateCredentials());
        registration.EnsureSuccessStatusCode();
        var mail = Assert.Single(factory.Sender.Messages);
        using var response = await client.PostAsJsonAsync("/api/auth/confirm-email", new { mail.UserId, mail.Token });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact(DisplayName = "送信失敗後も確認メールの再送でログイン可能になる")]
    public async Task ResendRecoversDeliveryFailure()
    {
        using var factory = new AuthFactory();
        factory.Sender.Fail = true;
        using var client = factory.CreateClient();
        using var registration = await Post(client, "register", CreateCredentials());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, registration.StatusCode);
        factory.Sender.Fail = false;
        using var resend = await Post(client, "resend-confirmation", new { CreateCredentials().Email });
        Assert.Equal(HttpStatusCode.OK, resend.StatusCode);
        var mail = Assert.Single(factory.Sender.Messages);
        using var confirmation = await Post(client, "confirm-email", new { mail.UserId, mail.Token });
        Assert.Equal(HttpStatusCode.NoContent, confirmation.StatusCode);
        using var login = await Post(client, "login", CreateCredentials());
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact(DisplayName = "確認済みと未登録の再送は同じ応答でメールを送らない")]
    public async Task ResendDoesNotDiscloseAccount()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        await Register(client, factory);
        using var confirmed = await Post(client, "resend-confirmation", new { CreateCredentials().Email });
        using var missing = await Post(client, "resend-confirmation", new { Email = "missing@example.com" });
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.Equal(confirmed.StatusCode, missing.StatusCode);
        Assert.Equal(await confirmed.Content.ReadAsStringAsync(), await missing.Content.ReadAsStringAsync());
        Assert.Single(factory.Sender.Messages);
    }

    private static Credentials CreateCredentials() => new("user@example.com", Password);

    // 前提条件の失敗を検証対象の失敗と混同しないよう、準備時にも応答を確認する。
    private static async Task<Credentials> Register(HttpClient client, AuthFactory factory)
    {
        var credentials = CreateCredentials();
        using var response = await Post(client, "register", credentials);
        response.EnsureSuccessStatusCode();
        var mail = Assert.Single(factory.Sender.Messages);
        using var confirmation = await Post(client, "confirm-email", new { mail.UserId, mail.Token });
        confirmation.EnsureSuccessStatusCode();
        return credentials;
    }

    private static async Task<Credentials> RegisterAndLogin(HttpClient client, AuthFactory factory)
    {
        var credentials = await Register(client, factory);
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

    private sealed class RecordingEmailSender : IConfirmationEmailSender
    {
        public List<(string Email, string UserId, string Token)> Messages { get; } = [];
        public bool Fail { get; set; }
        public Task SendAsync(string email, string userId, string token)
        {
            if (Fail) throw new InvalidOperationException("テスト用の送信失敗");
            Messages.Add((email, userId, token));
            return Task.CompletedTask;
        }
    }

    private sealed class AuthFactory : WebApplicationFactory<Program>
    {
        public RecordingEmailSender Sender { get; } = new();
        private readonly SqliteConnection connection = new("Data Source=:memory:");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            // SQL や正常系のログでテストの確認内容が埋もれないようにする。
            builder.ConfigureLogging(logging => logging.AddFilter<ConsoleLoggerProvider>(
                (_, level) => level >= LogLevel.Error));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IConfirmationEmailSender>();
                services.AddSingleton<IConfirmationEmailSender>(Sender);
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
