using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using backend.Data;
using backend.Services;
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

// AuthTests.cs の AuthFactory/RecordingEmailSender はそのクラス内の private ネストクラスのため直接再利用できない。
// DBを使うテストは個別のメモリDBで独立させる方針(AGENTS.md)に合わせ、同型のヘルパーをこのファイル内に用意する。
public class MfaTests
{
    private const string Password = "Test-password-123!";

    [Fact(DisplayName = "MFA未設定ではログインが即座に成功する")]
    public async Task LoginSucceedsImmediatelyWithoutMfa()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        var credentials = await Register(client, factory);
        using var response = await Post(client, "login", credentials);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResult>();
        Assert.False(body!.RequiresTwoFactor);
    }

    [Fact(DisplayName = "MFA設定の開始は未ログインでは拒否する")]
    public async Task SetupRejectsAnonymousUser()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        using var response = await Post(client, "mfa/setup");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "MFA設定開始時にCSRFトークンがなければ拒否する")]
    public async Task SetupRequiresCsrfToken()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        await RegisterAndLogin(client, factory);
        using var response = await client.PostAsync("/api/auth/mfa/setup", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact(DisplayName = "MFA無効時の状態取得は無効であることを返す")]
    public async Task StatusReportsDisabledByDefault()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        await RegisterAndLogin(client, factory);
        using var response = await client.GetAsync("/api/auth/mfa/status");
        response.EnsureSuccessStatusCode();
        var status = await response.Content.ReadFromJsonAsync<MfaStatus>();
        Assert.False(status!.Enabled);
        Assert.Equal(0, status.RecoveryCodesRemaining);
    }

    [Fact(DisplayName = "正しいコードでMFAを有効化できる")]
    public async Task EnableAcceptsCorrectCode()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        var (_, recoveryCodes, _) = await EnableMfa(client, factory);
        Assert.Equal(10, recoveryCodes.Count);

        using var status = await client.GetAsync("/api/auth/mfa/status");
        status.EnsureSuccessStatusCode();
        var body = await status.Content.ReadFromJsonAsync<MfaStatus>();
        Assert.True(body!.Enabled);
        Assert.Equal(10, body.RecoveryCodesRemaining);
    }

    [Fact(DisplayName = "誤ったコードではMFAを有効化できない")]
    public async Task EnableRejectsWrongCode()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        await RegisterAndLogin(client, factory);
        using var setupResponse = await Post(client, "mfa/setup");
        setupResponse.EnsureSuccessStatusCode();

        using var enableResponse = await Post(client, "mfa/enable", new { code = "000000" });
        Assert.Equal(HttpStatusCode.BadRequest, enableResponse.StatusCode);

        using var status = await client.GetAsync("/api/auth/mfa/status");
        status.EnsureSuccessStatusCode();
        Assert.False((await status.Content.ReadFromJsonAsync<MfaStatus>())!.Enabled);
    }

    [Fact(DisplayName = "有効化済みのMFAは再セットアップできない")]
    public async Task SetupRejectsAlreadyEnabled()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        await EnableMfa(client, factory);
        using var response = await Post(client, "mfa/setup");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact(DisplayName = "MFA有効なユーザーのログインは追加認証を要求する")]
    public async Task LoginRequiresTwoFactorWhenEnabled()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        var (credentials, _, _) = await EnableMfa(client, factory);
        await Logout(client);

        using var response = await Post(client, "login", credentials);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResult>();
        Assert.True(body!.RequiresTwoFactor);

        // パスワードのみでは認証が完了していない。
        using var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact(DisplayName = "正しい追加認証コードでログインを完了できる")]
    public async Task VerifyTwoFactorCompletesLogin()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        var (credentials, _, sharedKey) = await EnableMfa(client, factory);
        await Logout(client);
        using var login = await Post(client, "login", credentials);
        login.EnsureSuccessStatusCode();

        using var verify = await Post(client, "login/verify-2fa", new { code = GenerateTotpCode(sharedKey) });
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);

        using var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact(DisplayName = "誤った追加認証コードではログインを完了できない")]
    public async Task VerifyTwoFactorRejectsWrongCode()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        var (credentials, _, _) = await EnableMfa(client, factory);
        await Logout(client);
        using var login = await Post(client, "login", credentials);
        login.EnsureSuccessStatusCode();

        using var verify = await Post(client, "login/verify-2fa", new { code = "000000" });
        Assert.Equal(HttpStatusCode.Unauthorized, verify.StatusCode);
    }

    [Fact(DisplayName = "パスワード認証を経ずに追加認証コードだけでログインできない")]
    public async Task VerifyTwoFactorRejectsWithoutPendingLogin()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        using var response = await Post(client, "login/verify-2fa", new { code = "000000" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "追加認証コードの失敗を5回繰り返すとロックアウトする")]
    public async Task VerifyTwoFactorLocksOutAfterFiveFailures()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        var (credentials, _, sharedKey) = await EnableMfa(client, factory);
        await Logout(client);
        using var login = await Post(client, "login", credentials);
        login.EnsureSuccessStatusCode();

        for (var i = 0; i < 5; i++)
        {
            using var failure = await Post(client, "login/verify-2fa", new { code = "000000" });
            Assert.Equal(HttpStatusCode.Unauthorized, failure.StatusCode);
        }
        using var response = await Post(client, "login/verify-2fa", new { code = GenerateTotpCode(sharedKey) });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "リカバリーコードでログインを完了できる")]
    public async Task VerifyRecoveryCodeCompletesLogin()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        var (credentials, recoveryCodes, _) = await EnableMfa(client, factory);
        await Logout(client);
        using var login = await Post(client, "login", credentials);
        login.EnsureSuccessStatusCode();

        using var verify = await Post(client, "login/verify-recovery-code", new { code = recoveryCodes[0] });
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
    }

    [Fact(DisplayName = "使用済みのリカバリーコードは再利用できない")]
    public async Task VerifyRecoveryCodeRejectsReuse()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        var (credentials, recoveryCodes, _) = await EnableMfa(client, factory);
        await Logout(client);
        using var firstLogin = await Post(client, "login", credentials);
        firstLogin.EnsureSuccessStatusCode();
        using var firstVerify = await Post(client, "login/verify-recovery-code", new { code = recoveryCodes[0] });
        firstVerify.EnsureSuccessStatusCode();
        await Logout(client);

        using var secondLogin = await Post(client, "login", credentials);
        secondLogin.EnsureSuccessStatusCode();
        using var secondVerify = await Post(client, "login/verify-recovery-code", new { code = recoveryCodes[0] });
        Assert.Equal(HttpStatusCode.Unauthorized, secondVerify.StatusCode);
    }

    [Fact(DisplayName = "誤ったパスワードではMFAを無効化できない")]
    public async Task DisableRejectsWrongPassword()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        await EnableMfa(client, factory);
        using var response = await Post(client, "mfa/disable", new { password = "wrong-password" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact(DisplayName = "正しいパスワードでMFAを無効化するとログインが即座に成功する")]
    public async Task DisableAcceptsCorrectPassword()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        var (credentials, _, _) = await EnableMfa(client, factory);

        using var disable = await Post(client, "mfa/disable", new { password = Password });
        Assert.Equal(HttpStatusCode.NoContent, disable.StatusCode);

        await Logout(client);
        using var login = await Post(client, "login", credentials);
        login.EnsureSuccessStatusCode();
        var body = await login.Content.ReadFromJsonAsync<LoginResult>();
        Assert.False(body!.RequiresTwoFactor);
    }

    [Fact(DisplayName = "無効化後に再設定すると新しい鍵が発行され旧コードは使えない")]
    public async Task DisableThenSetupIssuesNewKey()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        var (_, _, oldSharedKey) = await EnableMfa(client, factory);
        using var disable = await Post(client, "mfa/disable", new { password = Password });
        disable.EnsureSuccessStatusCode();

        using var setupResponse = await Post(client, "mfa/setup");
        setupResponse.EnsureSuccessStatusCode();
        var setup = await setupResponse.Content.ReadFromJsonAsync<MfaSetup>();
        Assert.NotEqual(oldSharedKey, setup!.SharedKey);

        using var enableWithOldCode = await Post(client, "mfa/enable", new { code = GenerateTotpCode(oldSharedKey) });
        Assert.Equal(HttpStatusCode.BadRequest, enableWithOldCode.StatusCode);
    }

    [Fact(DisplayName = "誤ったパスワードではリカバリーコードを再生成できない")]
    public async Task RegenerateRecoveryCodesRejectsWrongPassword()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        await EnableMfa(client, factory);
        using var response = await Post(client, "mfa/recovery-codes", new { password = "wrong-password" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact(DisplayName = "正しいパスワードでリカバリーコードを再生成すると旧コードは無効になる")]
    public async Task RegenerateRecoveryCodesInvalidatesOldCodes()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        var (credentials, oldCodes, _) = await EnableMfa(client, factory);

        using var regenerate = await Post(client, "mfa/recovery-codes", new { password = Password });
        regenerate.EnsureSuccessStatusCode();
        var newCodes = (await regenerate.Content.ReadFromJsonAsync<RecoveryCodesResponse>())!.RecoveryCodes;
        Assert.Equal(10, newCodes.Count);
        Assert.DoesNotContain(oldCodes[0], newCodes);

        await Logout(client);
        using var login = await Post(client, "login", credentials);
        login.EnsureSuccessStatusCode();
        using var verify = await Post(client, "login/verify-recovery-code", new { code = oldCodes[0] });
        Assert.Equal(HttpStatusCode.Unauthorized, verify.StatusCode);
    }

    [Fact(DisplayName = "MFA関連エンドポイントへの過剰なリクエストはレート制限で拒否する")]
    public async Task MfaEndpointsEnforceRateLimit()
    {
        using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        await RegisterAndLogin(client, factory);
        HttpStatusCode lastStatus = HttpStatusCode.OK;
        for (var i = 0; i < 21; i++)
        {
            using var response = await Post(client, "mfa/setup");
            lastStatus = response.StatusCode;
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, lastStatus);
    }

    // --- ヘルパー ---

    private static Credentials CreateCredentials() => new("user@example.com", Password);

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

    private static async Task Logout(HttpClient client)
    {
        using var response = await Post(client, "logout");
        response.EnsureSuccessStatusCode();
    }

    // ログイン済みクライアントでMFAをセットアップ・有効化する。呼び出し後もクライアントはログイン状態のまま。
    private static async Task<(Credentials Credentials, List<string> RecoveryCodes, string SharedKey)> EnableMfa(
        HttpClient client, AuthFactory factory)
    {
        var credentials = await RegisterAndLogin(client, factory);
        using var setupResponse = await Post(client, "mfa/setup");
        setupResponse.EnsureSuccessStatusCode();
        var setup = (await setupResponse.Content.ReadFromJsonAsync<MfaSetup>())!;

        using var enableResponse = await Post(client, "mfa/enable", new { code = GenerateTotpCode(setup.SharedKey) });
        enableResponse.EnsureSuccessStatusCode();
        var recoveryCodes = (await enableResponse.Content.ReadFromJsonAsync<RecoveryCodesResponse>())!.RecoveryCodes;
        return (credentials, recoveryCodes, setup.SharedKey);
    }

    private static async Task<HttpResponseMessage> Post(HttpClient client, string endpoint, object? body = null)
    {
        var csrf = await client.GetFromJsonAsync<Csrf>("/api/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/auth/{endpoint}");
        request.Headers.Add("X-CSRF-TOKEN", csrf!.Token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    // ASP.NET Core Identity の AuthenticatorTokenProvider(内部の Rfc6238AuthenticationService)は internal のため、
    // テスト側で同じアルゴリズム(RFC 6238、HMAC-SHA1、30秒ステップ、6桁)を用いてコードを再現する。
    private static string GenerateTotpCode(string sharedKey, DateTimeOffset? at = null)
    {
        var key = Base32Decode(sharedKey.Replace(" ", ""));
        var counter = (long)((at ?? DateTimeOffset.UtcNow) - DateTimeOffset.UnixEpoch).TotalSeconds / 30;
        var counterBytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian) Array.Reverse(counterBytes);
        using var hmac = new HMACSHA1(key);
        var hash = hmac.ComputeHash(counterBytes);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6");
    }

    private static byte[] Base32Decode(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = 0;
        var value = 0;
        var output = new List<byte>();
        foreach (var c in input.TrimEnd('='))
        {
            var index = alphabet.IndexOf(char.ToUpperInvariant(c));
            if (index < 0) continue;
            value = (value << 5) | index;
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)((value >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }
        return [.. output];
    }

    private record Credentials(string Email, string Password);
    private record Csrf(string Token);
    private record LoginResult(bool RequiresTwoFactor);
    private record MfaStatus(bool Enabled, int RecoveryCodesRemaining);
    private record MfaSetup(string SharedKey, string OtpauthUri);
    private record RecoveryCodesResponse(List<string> RecoveryCodes);

    private sealed class RecordingEmailSender : IConfirmationEmailSender
    {
        public List<(string Email, string UserId, string Token)> Messages { get; } = [];
        public List<(string Email, string UserId, string Token)> PasswordResetMessages { get; } = [];
        public Task SendAsync(string email, string userId, string token)
        {
            Messages.Add((email, userId, token));
            return Task.CompletedTask;
        }
        public Task SendPasswordResetAsync(string email, string userId, string token)
        {
            PasswordResetMessages.Add((email, userId, token));
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

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) connection.Dispose();
        }
    }
}
