using backend.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace backend.Services;

// DataProtectorTokenProvider は IOptions<DataProtectionTokenProviderOptions>(名前なし)を直接参照するため、
// 名前付きオプションを登録するだけではメール確認用の有効期限と分離できない。専用の型を用意し、
// メール確認とは独立した有効期限を持てるようにする。
public sealed class PasswordResetTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public PasswordResetTokenProviderOptions() => Name = "PasswordResetTokenProvider";
}

public sealed class PasswordResetTokenProvider(IDataProtectionProvider dataProtectionProvider,
    IOptions<PasswordResetTokenProviderOptions> options, ILogger<DataProtectorTokenProvider<ApplicationUser>> logger)
    : DataProtectorTokenProvider<ApplicationUser>(dataProtectionProvider, options, logger);
