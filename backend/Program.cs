using System.Threading.RateLimiting;
using backend.Authorization;
using backend.Data;
using backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

// 設定ファイル・環境変数・起動引数を共通の構成として利用する。
var builder = WebApplication.CreateBuilder(args);

// 標準の CSRF 認可フィルターに必要な MVC サービスを登録する。
builder.Services.AddControllersWithViews(options =>
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));

// 開発時に API の仕様を確認できるよう OpenAPI の生成機能を登録する。
builder.Services.AddOpenApi();

// 不正な有効期限で稼働しないよう、確認メールの設定を起動時に検証する。
builder.Services.AddOptions<EmailOptions>()
    .Bind(builder.Configuration.GetSection("Email"))
    .Validate(options => options.ConfirmationTokenLifespanMinutes > 0,
        "Email:ConfirmationTokenLifespanMinutes は1以上の整数を設定してください。")
    .ValidateOnStart();

// メール配送を認証処理から分離し、SMTP 実装を差し替え可能にする。
builder.Services.AddTransient<IConfirmationEmailSender, SmtpConfirmationEmailSender>();

// 最初の管理者アカウントのブートストラップ設定。未設定なら何もしない(下記 AdminBootstrap 参照)。
builder.Services.AddOptions<AdminBootstrapOptions>()
    .Bind(builder.Configuration.GetSection("Admin:Bootstrap"));

// メール本文の案内と実際のトークン有効期限を同じ設定値に揃える。
builder.Services.AddOptions<DataProtectionTokenProviderOptions>()
    .Configure<IOptions<EmailOptions>>((options, emailOptions) =>
        options.TokenLifespan = TimeSpan.FromMinutes(emailOptions.Value.ConfirmationTokenLifespanMinutes));

// 認証情報を SQL Server に永続化し、接続文字列の未設定を検出する。
builder.Services.AddDbContext<AuthDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("AuthDatabase")
        ?? throw new InvalidOperationException("ConnectionStrings:AuthDatabase を設定してください。")));

// メール確認とパスワード要件・ロックアウトを Identity に統一して適用する。
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.User.RequireUniqueEmail = true;
    options.SignIn.RequireConfirmedEmail = true;
    options.Password.RequiredLength = 12;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
}).AddEntityFrameworkStores<AuthDbContext>().AddDefaultTokenProviders();

// ロールが保持するAPIアクション別の権限を、リクエストごとにDBから判定する認可基盤を登録する。
// Cookieには権限を一切載せないため、ロール・権限の変更が既存のログインセッションへ即時反映される(decisions/0001参照)。
builder.Services.AddAuthorization();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionAuthorizationPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

// 認証 Cookie の保護と有効期間を定め、SPA が扱える HTTP ステータスを返す。
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "try-svelte.auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = false;

    // API の未認証応答がログインページへのリダイレクトにならないようにする。
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };

    // SPA が権限不足と未認証を区別できるよう、拒否理由をステータスで伝える。
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});

// Cookie 認証を利用する更新 API を、別サイトからの不正なリクエストから保護する。
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "try-svelte.csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});

// 認証 API への過剰な試行を抑え、制限超過を HTTP 429 で通知する。
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // IP ごとに分離し、1人の攻撃者が他の利用者のログイン・登録を巻き添えにしないようにする。
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 20,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
});

// 登録済みのサービスと設定から、リクエストを処理するアプリを構築する。
var app = builder.Build();

// API 仕様の公開を開発環境に限定する。
if (app.Environment.IsDevelopment()) app.MapOpenApi();

// 平文 HTTP でのアクセスを HTTPS へ誘導する。
app.UseHttpsRedirection();

// ディレクトリへのアクセスで SPA の index.html を配信できるようにする。
app.UseDefaultFiles();

// ビルド済み SPA のファイルを ASP.NET Core から配信する。
app.UseStaticFiles();

// エンドポイントに指定した制限を、認証処理やコントローラーの実行前に適用する。
app.UseRateLimiter();

// 認可判定に先立ち、認証 Cookie から利用者を特定する。
app.UseAuthentication();

// エンドポイントの認可要件に従い、利用者のアクセスを判定する。
app.UseAuthorization();

// コントローラーで定義した API のルートを公開する。
app.MapControllers();

// API の誤った URL に SPA の HTML を返さない。
app.MapFallback("/api/{**path}", () => Results.NotFound());

// SPA 内の URL を直接開いた場合も、クライアント側のルーティングに委ねる。
app.MapFallbackToFile("index.html");

// コード上の [PermissionKey] を PermissionActions テーブルへ同期する。
// スキーマ変更ではなくデータ同期のため、マイグレーションの事前適用方針とは別に起動時に実行する。
await PermissionActionSync.RunAsync(app.Services);

// 最初の管理者アカウントをブートストラップする(Admin.Roles を持つロールが既にあれば何もしない)。
// Admin.Roles の PermissionAction 行が必要なため、PermissionActionSync より後に実行する。
await AdminBootstrap.RunAsync(app.Services);

// ホストを起動し、終了要求までリクエストを受け付ける。
app.Run();

// 統合テストの WebApplicationFactory からエントリーポイントを参照可能にする。
public partial class Program { }
