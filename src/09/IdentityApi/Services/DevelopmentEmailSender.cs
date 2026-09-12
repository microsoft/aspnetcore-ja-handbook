using System.Net;
using Microsoft.AspNetCore.Identity;

namespace IdentityApi.Services;

// 開発専用。確認リンクやリセットコードを含むログは秘密情報のため、公開・共有しないでください。
public sealed class DevelopmentEmailSender(ILogger<DevelopmentEmailSender> logger)
    : IEmailSender<IdentityUser>
{
    public Task SendConfirmationLinkAsync(IdentityUser user, string email, string confirmationLink)
    {
        logger.LogInformation("Development-only confirmation link for {Email}: {Link}",
            email, WebUtility.HtmlDecode(confirmationLink));
        return Task.CompletedTask;
    }

    public Task SendPasswordResetLinkAsync(IdentityUser user, string email, string resetLink)
    {
        logger.LogInformation("Development-only password reset link for {Email}: {Link}",
            email, WebUtility.HtmlDecode(resetLink));
        return Task.CompletedTask;
    }

    public Task SendPasswordResetCodeAsync(IdentityUser user, string email, string resetCode)
    {
        logger.LogInformation("Development-only password reset code for {Email}: {Code}", email, resetCode);
        return Task.CompletedTask;
    }
}
