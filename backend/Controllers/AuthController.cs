using System.ComponentModel.DataAnnotations;
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
public class AuthController(UserManager<IdentityUser> users,
    SignInManager<IdentityUser> signIn, IAntiforgery antiforgery) : ControllerBase
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
        var user = new IdentityUser { UserName = request.Email.Trim(), Email = request.Email.Trim() };
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
        return StatusCode(StatusCodes.Status201Created);
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
        return Ok(new { id = user!.Id, email = user.Email });
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
        return Ok(new { id = user.Id, email = user.Email });
    }

    // 期限切れ後でもブラウザーの Cookie を削除できるよう匿名アクセスを許可する。
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await signIn.SignOutAsync();
        return NoContent();
    }

    public sealed record Credentials(
        [Required, EmailAddress, StringLength(254)] string Email,
        [Required, StringLength(128)] string Password);
}
