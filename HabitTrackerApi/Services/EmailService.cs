using Microsoft.Extensions.Configuration;
using System.Net.Mail;
using System.Net;



namespace Services;

public class EmailService
{
    private readonly IConfiguration _configuration;

    public EmailService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task SendEmailAsync(string toEmail, string subject, string body)
    {
        var smtpHost = _configuration["Email:SmtpHost"] ?? throw new InvalidOperationException("Email:SmtpHost ayarlanmamış.");
        var smtpPortText = _configuration["Email:SmtpPort"] ?? throw new InvalidOperationException("Email:SmtpPort ayarlanmamış.");
        var senderEmail = _configuration["Email:SenderEmail"] ?? throw new InvalidOperationException("Email:SenderEmail ayarlanmamış.");
        var senderPassword = _configuration["Email:SenderPassword"] ?? throw new InvalidOperationException("Email:SenderPassword ayarlanmamış.");
        var senderName = _configuration["Email:SenderName"] ?? "HabitTracker";

        using var client = new SmtpClient(smtpHost, int.Parse(smtpPortText))
        {
            Credentials = new NetworkCredential(senderEmail, senderPassword),
            EnableSsl = true
        };

        var mailMessage = new MailMessage
        {
            From = new MailAddress(senderEmail, senderName),
            Subject = subject,
            Body = body,
            IsBodyHtml = true
        };

        mailMessage.To.Add(toEmail);
        await client.SendMailAsync(mailMessage);
    }
}