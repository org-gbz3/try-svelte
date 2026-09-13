using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace backend.Services;

public sealed class EmailOptions
{
    public string PublicBaseUrl { get; set; } = "";
    public int ConfirmationTokenLifespanMinutes { get; set; } = 1440;
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
}

public sealed class SmtpConfirmationEmailSender(IOptions<EmailOptions> options,
    IHostEnvironment environment) : IConfirmationEmailSender
{
    public async Task SendAsync(string email, string userId, string token)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.From)
            || !Uri.TryCreate(settings.PublicBaseUrl, UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != "https" && !(environment.IsDevelopment() && baseUri.Scheme == "http"))
            || !string.IsNullOrEmpty(baseUri.Query) || !string.IsNullOrEmpty(baseUri.Fragment)
            || !string.IsNullOrEmpty(baseUri.UserInfo))
            throw new InvalidOperationException("Email の SMTP 設定と PublicBaseUrl を確認してください。");

        // フラグメントに入れ、確認トークンがアクセスログや Referer に流れるのを避ける。
        var link = $"{settings.PublicBaseUrl.TrimEnd('/')}/confirm-email#userId={Uri.EscapeDataString(userId)}&token={Uri.EscapeDataString(token)}";
        using var message = new MailMessage(settings.From, email)
        {
            Subject = "メールアドレスの確認",
            Body = $"以下のリンクを開き、メールアドレスの確認ボタンを押してください。リンクの有効期限は発行から{settings.ConfirmationTokenLifespanMinutes}分です。\n\n{link}\n\n心当たりがない場合は、このメールを破棄してください。"
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
