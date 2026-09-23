using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace backend.Services;

public sealed class EmailOptions
{
    public string PublicBaseUrl { get; set; } = "";
    public int ConfirmationTokenLifespanMinutes { get; set; } = 1440;
    public int PasswordResetTokenLifespanMinutes { get; set; } = 30;
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string From { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public interface IConfirmationEmailSender
{
    Task SendAsync(string email, string userId, string token);
    Task SendPasswordResetAsync(string email, string userId, string token);
}

public sealed class SmtpConfirmationEmailSender(IOptions<EmailOptions> options,
    IHostEnvironment environment) : IConfirmationEmailSender
{
    public Task SendAsync(string email, string userId, string token) => SendLinkAsync(
        email, "メールアドレスの確認", "confirm-email",
        userId, token,
        (settings, link) => $"以下のリンクを開き、メールアドレスの確認ボタンを押してください。リンクの有効期限は発行から{settings.ConfirmationTokenLifespanMinutes}分です。\n\n{link}\n\n心当たりがない場合は、このメールを破棄してください。");

    public Task SendPasswordResetAsync(string email, string userId, string token) => SendLinkAsync(
        email, "パスワードの再設定", "reset-password",
        userId, token,
        (settings, link) => $"以下のリンクを開き、新しいパスワードを設定してください。リンクの有効期限は発行から{settings.PasswordResetTokenLifespanMinutes}分です。\n\n{link}\n\n心当たりがない場合は、このメールを破棄してください。");

    private async Task SendLinkAsync(string email, string subject, string routePath, string userId, string token,
        Func<EmailOptions, string, string> buildBody)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.From)
            || !Uri.TryCreate(settings.PublicBaseUrl, UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != "https" && !(environment.IsDevelopment() && baseUri.Scheme == "http"))
            || !string.IsNullOrEmpty(baseUri.Query) || !string.IsNullOrEmpty(baseUri.Fragment)
            || !string.IsNullOrEmpty(baseUri.UserInfo))
            throw new InvalidOperationException("Email の SMTP 設定と PublicBaseUrl を確認してください。");

        // フラグメントに入れ、トークンがアクセスログや Referer に流れるのを避ける。
        var link = $"{settings.PublicBaseUrl.TrimEnd('/')}/{routePath}#userId={Uri.EscapeDataString(userId)}&token={Uri.EscapeDataString(token)}";
        using var message = new MailMessage(settings.From, email)
        {
            Subject = subject,
            Body = buildBody(settings, link)
        };
        using var client = new SmtpClient(settings.Host, settings.Port)
        {
            EnableSsl = settings.EnableSsl,
            UseDefaultCredentials = false
        };
        if (!string.IsNullOrEmpty(settings.Username))
            client.Credentials = new NetworkCredential(settings.Username, settings.Password);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await client.SendMailAsync(message, timeout.Token);
    }
}
