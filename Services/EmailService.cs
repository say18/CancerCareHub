
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace CancerCareHub.Services;

public class EmailService : IEmailService
{
    private readonly IConfiguration _configuration;

    public EmailService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task SendEmailAsync(
        string email,
        string subject,
        string message)
    {
        var host = _configuration["EmailSettings:Host"]
            ?? throw new InvalidOperationException("SMTP host missing.");

        var port = int.Parse(
            _configuration["EmailSettings:Port"] ?? "587");

        var username = _configuration["EmailSettings:Username"]
            ?? throw new InvalidOperationException("SMTP username missing.");

        var password = _configuration["EmailSettings:Password"]
            ?? throw new InvalidOperationException("SMTP password missing.");

        var fromEmail = _configuration["EmailSettings:FromEmail"]
            ?? throw new InvalidOperationException("Sender email missing.");

        var fromName = _configuration["EmailSettings:FromName"]
            ?? "Cancer Care Hub Bangladesh";

        var emailMessage = new MimeMessage();

        emailMessage.From.Add(
            new MailboxAddress(fromName, fromEmail));

        emailMessage.To.Add(
            MailboxAddress.Parse(email));

        emailMessage.Subject = subject;

        emailMessage.Body = new TextPart("plain")
        {
            Text = message
        };

        using var client = new SmtpClient();

        await client.ConnectAsync(
            host,
            port,
            SecureSocketOptions.StartTls
        );

        await client.AuthenticateAsync(username, password);

        await client.SendAsync(emailMessage);

        await client.DisconnectAsync(true);
    }
}
