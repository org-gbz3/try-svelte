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

    private async Task<bool> SendConfirmation(ApplicationUser user)
    {
        try
        {
            var token = await users.GenerateEmailConfirmationTokenAsync(user);
            await emailSender.SendAsync(user.Email!, user.Id, token);
            return true;
        }
        catch (Exception exception)
        {
            var smtpStatus = exception is SmtpException smtp ? smtp.StatusCode.ToString() : "該当なし";
            if (environment.IsDevelopment())
            {
                // SMTP 応答と内部例外が原因特定に必要なため、開発環境だけで詳細を記録する。
                logger.LogError(exception,
                    "確認メールの送信に失敗しました。種類: {ExceptionType}, SMTP ステータス: {SmtpStatus}",
                    exception.GetType().Name, smtpStatus);
            }
            else
            {
                // SMTP 応答に宛先などが含まれる可能性があるため、本番では例外本文を記録しない。
                logger.LogError(
                    "確認メールの送信に失敗しました。種類: {ExceptionType}, SMTP ステータス: {SmtpStatus}",
                    exception.GetType().Name, smtpStatus);
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
        if (!result.Succeeded)
            return Unauthorized(new { message = "ログインできません。入力内容を確認するか、しばらく待って再試行してください。" });
        return Ok(new { id = user!.Id, email = user.Email, permissions = await EffectivePermissionsAsync(user.Id) });
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

    public sealed record Credentials(
        [Required, EmailAddress, StringLength(254)] string Email,
        [Required, StringLength(128)] string Password);
}
