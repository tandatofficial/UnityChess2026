using System.Net;
using System.Net.Mail;

namespace ChessServer.Auth;

public sealed class EmailSender(IConfiguration configuration)
{
    public async Task SendVerificationAsync(string email, string code, CancellationToken cancellationToken)
    {
        var emailUser = configuration["EMAIL_USER"];
        var emailPass = configuration["EMAIL_PASS"];
        var mailtrapUser = configuration["MAILTRAP_USER"];
        var mailtrapPass = configuration["MAILTRAP_PASS"];
        var gmail = !string.IsNullOrWhiteSpace(emailUser) &&
                    !emailUser.Contains("your_gmail", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(emailPass) ||
                    !string.IsNullOrWhiteSpace(mailtrapUser) &&
                    mailtrapUser.Contains("@gmail.com", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(mailtrapPass);
        var mailtrap = !string.IsNullOrWhiteSpace(mailtrapUser) && !string.IsNullOrWhiteSpace(mailtrapPass) &&
                       !mailtrapUser.Contains("your_mailtrap", StringComparison.OrdinalIgnoreCase) &&
                       !mailtrapPass.Contains("your_mailtrap", StringComparison.OrdinalIgnoreCase) &&
                       !mailtrapUser.Contains("@gmail.com", StringComparison.OrdinalIgnoreCase);
        if (!gmail && !mailtrap) throw new InvalidOperationException("Email delivery is not configured.");

        var from = gmail ? emailUser ?? mailtrapUser! : "no-reply@chessgame.com";
        var message = new MailMessage(from!, email)
        {
            Subject = "Chess Game - Verify your registration",
            Body = $"Your verification code is: {code}. It will expire in 10 minutes.",
            IsBodyHtml = false
        };
        using var client = new SmtpClient(gmail ? "smtp.gmail.com" : "sandbox.smtp.mailtrap.io", gmail ? 587 : 2525)
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(gmail ? emailUser ?? mailtrapUser! : mailtrapUser!, gmail ? emailPass ?? mailtrapPass! : mailtrapPass!)
        };
        await client.SendMailAsync(message, cancellationToken);
    }
}
