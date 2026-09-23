using System.ComponentModel.DataAnnotations;
using System.Net.Mail;
using backend.Authorization;
using backend.Data;
using backend.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace backend.Controllers;

[ApiController]
[Route("api/auth")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class AuthController(UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn, IAntiforgery antiforgery,
    IConfirmationEmailSender emailSender, ILogger<AuthController> logger,
    IHostEnvironment environment, AuthDbContext db) : ControllerBase
{
    [HttpGet("csrf")]
    public IActionResult Csrf() => Ok(new
    {
        token = antiforgery.GetAndStoreTokens(HttpContext).RequestToken
    });

    [EnableRateLimiting("auth")]
    [HttpPost("register")]
    public async Task<IActionResult> Register(Credentials request)
    {
        var user = new ApplicationUser
        {
            UserName = request.Email.Trim(),
            Email = request.Email.Trim(),
            CreatedAt = DateTime.UtcNow
        };
        IdentityResult result;
        try
        {
            result = await users.CreateAsync(user, request.Password);
        }
        // 同時登録による一意制約違反も通常の登録失敗として扱う。
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return BadRequest(new { message = "このメールアドレスでは登録できません。" });
        }
        if (!result.Succeeded)
        {
            var passwordError = result.Errors.Any(error => error.Code.StartsWith("Password"));
            return BadRequest(new { message = passwordError
                ? "パスワードは12文字以上で、大文字・小文字・数字・記号を含めてください。"
                : "このメールアドレスでは登録できません。" });
        }
        if (!await SendConfirmation(user))
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                message = "アカウントは作成されましたが、確認メールを送信できませんでした。ログイン画面から再送してください。"
            });
        return StatusCode(StatusCodes.Status201Created);
    }

    [EnableRateLimiting("auth")]
    [HttpPost("confirm-email")]
    public async Task<IActionResult> ConfirmEmail(Confirmation request)
    {
        var user = await users.FindByIdAsync(request.UserId);
        if (user is null || !(await users.ConfirmEmailAsync(user, request.Token)).Succeeded)
            return BadRequest(new { message = "確認リンクが無効か期限切れです。確認メールを再送してください。" });
        return NoContent();
    }

    [EnableRateLimiting("auth")]
    [HttpPost("resend-confirmation")]
    public async Task<IActionResult> ResendConfirmation(EmailRequest request)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is not null && !await users.IsEmailConfirmedAsync(user))
            await SendConfirmation(user);
        // 未登録・確認済み・配送失敗でも、アカウントの有無を応答で公開しない。
        return Ok(new { message = "未確認のアカウントがある場合、確認メールを送信します。届かない場合は時間をおいて再試行してください。" });
    }

    [EnableRateLimiting("auth")]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(EmailRequest request)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is not null)
            await SendPasswordReset(user);
        // 登録の有無・確認状態を応答で公開しない(resend-confirmationと同じ方針)。
        return Ok(new { message = "登録されたメールアドレスの場合、パスワード再設定用のメールを送信します。届かない場合は時間をおいて再試行してください。" });
    }

    [EnableRateLimiting("auth")]
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request)
    {
        var user = await users.FindByIdAsync(request.UserId);
        if (user is null)
            return BadRequest(new { message = "再設定リンクが無効か期限切れです。もう一度お試しください。" });

        // ResetPasswordAsync と、未確認アカウントを確認済みにする更新の2回SaveChangesが走るため、
        // 途中で失敗しても中途半端な状態にならないよう明示的トランザクションで囲む(decisions/0002参照)。
        await using var transaction = await db.Database.BeginTransactionAsync();
        var result = await users.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!result.Succeeded)
        {
            var passwordError = result.Errors.Any(error => error.Code.StartsWith("Password"));
            return BadRequest(new { message = passwordError
                ? "パスワードは12文字以上で、大文字・小文字・数字・記号を含めてください。"
                : "再設定リンクが無効か期限切れです。もう一度お試しください。" });
        }
        // 再設定メールのリンクを開けたことは、メール確認と同水準の所有証明とみなす。
        if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            await users.UpdateAsync(user);
        }
        await transaction.CommitAsync();

        // パスワード再設定によりSecurityStampが更新され、他セッションも既定の検証間隔で失効するが、
        // このブラウザーのセッションは念のため即座に終了する。
        await signIn.SignOutAsync();
        return NoContent();
    }

    private Task<bool> SendConfirmation(ApplicationUser user) => SendEmailAsync(
        "確認メール", async () =>
        {
            var token = await users.GenerateEmailConfirmationTokenAsync(user);
            await emailSender.SendAsync(user.Email!, user.Id, token);
        });

    private Task<bool> SendPasswordReset(ApplicationUser user) => SendEmailAsync(
        "パスワード再設定メール", async () =>
        {
            var token = await users.GeneratePasswordResetTokenAsync(user);
            await emailSender.SendPasswordResetAsync(user.Email!, user.Id, token);
        });

    private async Task<bool> SendEmailAsync(string emailKind, Func<Task> send)
    {
        try
        {
            await send();
            return true;
        }
        catch (Exception exception)
        {
            var smtpStatus = exception is SmtpException smtp ? smtp.StatusCode.ToString() : "該当なし";
            if (environment.IsDevelopment())
            {
                // SMTP 応答と内部例外が原因特定に必要なため、開発環境だけで詳細を記録する。
                logger.LogError(exception,
                    "{EmailKind}の送信に失敗しました。種類: {ExceptionType}, SMTP ステータス: {SmtpStatus}",
                    emailKind, exception.GetType().Name, smtpStatus);
            }
            else
            {
                // SMTP 応答に宛先などが含まれる可能性があるため、本番では例外本文を記録しない。
                logger.LogError(
                    "{EmailKind}の送信に失敗しました。種類: {ExceptionType}, SMTP ステータス: {SmtpStatus}",
                    emailKind, exception.GetType().Name, smtpStatus);
            }
            return false;
        }
    }

    [EnableRateLimiting("auth")]
    [HttpPost("login")]
    public async Task<IActionResult> Login(Credentials request)
    {
        var user = await users.FindByNameAsync(request.Email.Trim());
        var result = user is null
            ? Microsoft.AspNetCore.Identity.SignInResult.Failed
            : await signIn.PasswordSignInAsync(user, request.Password, isPersistent: false, lockoutOnFailure: true);

        // パスワードは正しいが追加認証が必要。この時点でIdentityは既に一時的な2FA保留Cookieを発行済み。
        // 401にすると apiFetch の「401で認証状態を匿名化する」副作用と衝突し、パスワード誤りとも
        // 区別できなくなるため、200 OK+フラグで明示的に分岐させる(decisions/0008参照)。
        if (result.RequiresTwoFactor)
            return Ok(new { requiresTwoFactor = true });

        if (!result.Succeeded)
            return Unauthorized(new { message = "ログインできません。入力内容を確認するか、しばらく待って再試行してください。" });
        return Ok(new { id = user!.Id, email = user.Email, permissions = await EffectivePermissionsAsync(user.Id) });
    }

    [EnableRateLimiting("auth")]
    [HttpPost("login/verify-2fa")]
    public async Task<IActionResult> VerifyTwoFactor(TwoFactorCodeRequest request)
    {
        var user = await signIn.GetTwoFactorAuthenticationUserAsync();
        if (user is null)
            return Unauthorized(new { message = "ログインできません。入力内容を確認するか、しばらく待って再試行してください。" });

        var code = request.Code.Replace(" ", "").Replace("-", "");
        var result = await signIn.TwoFactorAuthenticatorSignInAsync(code, isPersistent: false, rememberClient: false);
        if (!result.Succeeded)
            return Unauthorized(new { message = "ログインできません。入力内容を確認するか、しばらく待って再試行してください。" });
        return Ok(new { id = user.Id, email = user.Email, permissions = await EffectivePermissionsAsync(user.Id) });
    }

    [EnableRateLimiting("auth")]
    [HttpPost("login/verify-recovery-code")]
    public async Task<IActionResult> VerifyRecoveryCode(RecoveryCodeRequest request)
    {
        var user = await signIn.GetTwoFactorAuthenticationUserAsync();
        if (user is null)
            return Unauthorized(new { message = "ログインできません。入力内容を確認するか、しばらく待って再試行してください。" });

        var result = await signIn.TwoFactorRecoveryCodeSignInAsync(request.Code.Trim());
        if (!result.Succeeded)
            return Unauthorized(new { message = "ログインできません。入力内容を確認するか、しばらく待って再試行してください。" });
        return Ok(new { id = user.Id, email = user.Email, permissions = await EffectivePermissionsAsync(user.Id) });
    }

    [EnableRateLimiting("auth")]
    [Authorize]
    [HttpGet("mfa/status")]
    public async Task<IActionResult> MfaStatus()
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Unauthorized();
        return Ok(new
        {
            enabled = user.TwoFactorEnabled,
            recoveryCodesRemaining = user.TwoFactorEnabled ? await users.CountRecoveryCodesAsync(user) : 0
        });
    }

    [EnableRateLimiting("auth")]
    [Authorize]
    [HttpPost("mfa/setup")]
    public async Task<IActionResult> SetupMfa()
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Unauthorized();
        if (user.TwoFactorEnabled)
            return BadRequest(new { message = "MFAは既に有効です。無効化してから再設定してください。" });

        // 未確定のセットアップ(スキャン後に確認コードを入力せず離脱した等)を毎回破棄し、常に新しい鍵を発行する。
        // TwoFactorEnabled=falseの間は有効な秘密鍵として使われていないため、都度リセットしても安全。
        await users.ResetAuthenticatorKeyAsync(user);
        var unformattedKey = await users.GetAuthenticatorKeyAsync(user);

        var issuer = users.Options.Tokens.AuthenticatorIssuer;
        var otpauthUri = $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(user.Email!)}" +
                          $"?secret={unformattedKey}&issuer={Uri.EscapeDataString(issuer)}&digits=6";

        return Ok(new { sharedKey = FormatKey(unformattedKey!), otpauthUri });
    }

    [EnableRateLimiting("auth")]
    [Authorize]
    [HttpPost("mfa/enable")]
    public async Task<IActionResult> EnableMfa(EnableMfaRequest request)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Unauthorized();
        if (user.TwoFactorEnabled)
            return BadRequest(new { message = "MFAは既に有効です。" });

        var code = request.Code.Replace(" ", "").Replace("-", "");
        var valid = await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider!, code);
        if (!valid)
            return BadRequest(new { message = "コードが正しくありません。もう一度お試しください。" });

        // SetTwoFactorEnabledAsync と GenerateNewTwoFactorRecoveryCodesAsync の2回SaveChangesAsyncが走るため、
        // 明示トランザクションで囲む(decisions/0002参照)。
        await using var transaction = await db.Database.BeginTransactionAsync();
        await users.SetTwoFactorEnabledAsync(user, true);
        var recoveryCodes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        await transaction.CommitAsync();

        return Ok(new { recoveryCodes });
    }

    [EnableRateLimiting("auth")]
    [Authorize]
    [HttpPost("mfa/disable")]
    public async Task<IActionResult> DisableMfa(ReauthRequest request)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Unauthorized();

        // 認証アプリを紛失していても無効化できるよう、TOTPコードではなくパスワードで再認証する。
        var reauth = await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!reauth.Succeeded)
            return BadRequest(new { message = "現在のパスワードが正しくありません。" });

        // SetTwoFactorEnabledAsync と ResetAuthenticatorKeyAsync の2回SaveChangesAsyncが走るため、
        // 明示トランザクションで囲む(decisions/0002参照)。
        await using var transaction = await db.Database.BeginTransactionAsync();
        await users.SetTwoFactorEnabledAsync(user, false);
        // 秘密鍵を破棄し、誤って再有効化されても古い鍵が使い回されないようにする。
        await users.ResetAuthenticatorKeyAsync(user);
        await transaction.CommitAsync();

        return NoContent();
    }

    [EnableRateLimiting("auth")]
    [Authorize]
    [HttpPost("mfa/recovery-codes")]
    public async Task<IActionResult> RegenerateRecoveryCodes(ReauthRequest request)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Unauthorized();
        if (!user.TwoFactorEnabled)
            return BadRequest(new { message = "MFAが有効なときのみ実行できます。" });

        var reauth = await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!reauth.Succeeded)
            return BadRequest(new { message = "現在のパスワードが正しくありません。" });

        // 1回のSaveChangesAsyncで完結するため明示トランザクション不要(decisions/0002参照)。
        var recoveryCodes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        return Ok(new { recoveryCodes });
    }

    // 手動入力用に4文字ごとにスペースを挿入する(Identity UI scaffoldingの表示形式に合わせる)。
    private static string FormatKey(string unformattedKey)
    {
        var result = new System.Text.StringBuilder();
        var remaining = unformattedKey;
        while (remaining.Length > 0)
        {
            var chunkLength = Math.Min(4, remaining.Length);
            result.Append(remaining[..chunkLength]).Append(' ');
            remaining = remaining[chunkLength..];
        }
        return result.ToString().TrimEnd();
    }

    private const int MaxPasskeysPerUser = 10;

    [EnableRateLimiting("auth")]
    [Authorize]
    [HttpPost("passkeys/registration-options")]
    public async Task<IActionResult> PasskeyRegistrationOptions()
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Unauthorized();
        var optionsJson = await signIn.MakePasskeyCreationOptionsAsync(new PasskeyUserEntity
        {
            Id = user.Id,
            Name = user.Email!,
            DisplayName = user.Email!
        });
        return Content(optionsJson, "application/json");
    }

    [EnableRateLimiting("auth")]
    [Authorize]
    [HttpPost("passkeys")]
    public async Task<IActionResult> RegisterPasskey(PasskeyRegistrationRequest request)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Unauthorized();

        // 事前に registration-options を呼ばずに(または期限切れの状態で)呼び出すと
        // Identity は失敗結果ではなく例外を投げるため、他の失敗と同じ応答に揃える。
        PasskeyAttestationResult attestation;
        try
        {
            attestation = await signIn.PerformPasskeyAttestationAsync(request.CredentialJson);
        }
        catch (InvalidOperationException exception)
        {
            logger.LogWarning(exception, "パスキー登録の呼び出し順序が不正です。");
            return BadRequest(new { message = "パスキーの登録に失敗しました。もう一度お試しください。" });
        }
        if (!attestation.Succeeded)
        {
            // 原因(RPID・Origin不一致、署名検証失敗など)はユーザーには詳細を返さず、調査用にログへ残す。
            logger.LogWarning("パスキーの登録に失敗しました。理由: {Reason}", attestation.Failure?.Message);
            return BadRequest(new { message = "パスキーの登録に失敗しました。もう一度お試しください。" });
        }

        // DB枯渇攻撃を防ぐため、1ユーザーあたりの登録数に上限を設ける。
        var existing = await users.GetPasskeysAsync(user);
        if (existing.Count >= MaxPasskeysPerUser)
            return BadRequest(new { message = $"登録できるパスキーは{MaxPasskeysPerUser}件までです。不要なパスキーを削除してから再試行してください。" });

        var passkey = attestation.Passkey;
        passkey.Name = string.IsNullOrWhiteSpace(request.Name) ? "パスキー" : request.Name.Trim();
        var result = await users.AddOrUpdatePasskeyAsync(user, passkey);
        if (!result.Succeeded)
            return BadRequest(new { message = "パスキーを保存できませんでした。" });
        return StatusCode(StatusCodes.Status201Created);
    }

    [Authorize]
    [HttpGet("passkeys")]
    public async Task<IActionResult> ListPasskeys()
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Unauthorized();
        var passkeys = await users.GetPasskeysAsync(user);
        return Ok(passkeys.Select(passkey => new
        {
            id = ToBase64Url(passkey.CredentialId),
            name = passkey.Name,
            createdAt = passkey.CreatedAt,
            isBackedUp = passkey.IsBackedUp
        }));
    }

    [EnableRateLimiting("auth")]
    [Authorize]
    [HttpDelete("passkeys/{credentialId}")]
    public async Task<IActionResult> RemovePasskey(string credentialId)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Unauthorized();
        var result = await users.RemovePasskeyAsync(user, FromBase64Url(credentialId));
        if (!result.Succeeded)
            return BadRequest(new { message = "パスキーを削除できませんでした。" });
        return NoContent();
    }

    // パスワードを問わずログイン可能なため、未登録・未対応も既存のパスワードログインと同じ一般的な401にまとめる。
    [EnableRateLimiting("auth")]
    [HttpPost("passkeys/login-options")]
    public async Task<IActionResult> PasskeyLoginOptions(EmailRequest request)
    {
        var user = await users.FindByNameAsync(request.Email.Trim());
        var optionsJson = await signIn.MakePasskeyRequestOptionsAsync(user);
        return Content(optionsJson, "application/json");
    }

    [EnableRateLimiting("auth")]
    [HttpPost("passkeys/login")]
    public async Task<IActionResult> PasskeyLogin(PasskeyLoginRequest request)
    {
        // 事前に login-options を呼ばずに(または期限切れの状態で)呼び出すと
        // Identity は失敗結果ではなく例外を投げるため、他の失敗と同じ応答に揃える。
        Microsoft.AspNetCore.Identity.SignInResult result;
        try
        {
            result = await signIn.PasskeySignInAsync(request.CredentialJson);
        }
        catch (InvalidOperationException exception)
        {
            logger.LogWarning(exception, "パスキーログインの呼び出し順序が不正です。");
            return Unauthorized(new { message = "ログインできません。入力内容を確認するか、しばらく待って再試行してください。" });
        }
        if (!result.Succeeded)
        {
            logger.LogWarning("パスキーログインに失敗しました。結果: {Result}", result);
            return Unauthorized(new { message = "ログインできません。入力内容を確認するか、しばらく待って再試行してください。" });
        }
        var user = await users.GetUserAsync(User);
        if (user is null) return Unauthorized();
        return Ok(new { id = user.Id, email = user.Email, permissions = await EffectivePermissionsAsync(user.Id) });
    }

    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
        return Convert.FromBase64String(base64);
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
        {
            await signIn.SignOutAsync();
            return Unauthorized();
        }
        return Ok(new { id = user.Id, email = user.Email, permissions = await EffectivePermissionsAsync(user.Id) });
    }

    // ログイン中ユーザーが保持する全ロールの実効権限(アクションキー→最大レベル)。
    // フロントエンドがナビ表示を権限で出し分けられるよう /login・/me の両方で返す(decisions/0003参照)。
    private async Task<Dictionary<string, PermissionLevel>> EffectivePermissionsAsync(string userId)
    {
        var levels = await PermissionQueries.ForUser(db, userId).ToListAsync();
        return levels
            .GroupBy(permission => permission.ActionKey)
            .ToDictionary(group => group.Key, group => group.Max(permission => permission.Level));
    }

    // 期限切れ後でもブラウザーの Cookie を削除できるよう匿名アクセスを許可する。
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await signIn.SignOutAsync();
        return NoContent();
    }

    public sealed record Confirmation(
        [Required, StringLength(128)] string UserId,
        [Required, StringLength(4096)] string Token);

    public sealed record EmailRequest([Required, EmailAddress, StringLength(254)] string Email);

    public sealed record ResetPasswordRequest(
        [Required, StringLength(128)] string UserId,
        [Required, StringLength(4096)] string Token,
        [Required, StringLength(128)] string NewPassword);

    public sealed record PasskeyRegistrationRequest(
        [Required] string CredentialJson,
        [StringLength(64)] string? Name);

    public sealed record PasskeyLoginRequest([Required] string CredentialJson);

    public sealed record Credentials(
        [Required, EmailAddress, StringLength(254)] string Email,
        [Required, StringLength(128)] string Password);

    public sealed record TwoFactorCodeRequest([Required, StringLength(6, MinimumLength = 6)] string Code);

    public sealed record RecoveryCodeRequest([Required, StringLength(32)] string Code);

    public sealed record ReauthRequest([Required, StringLength(128)] string Password);

    public sealed record EnableMfaRequest([Required, StringLength(6, MinimumLength = 6)] string Code);
}
