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

    public async Task SendEmailAsync(string toEmail,string subject,string body)
    {
        using var client = new SmtpClient(_configuration["Email:SmtpHost"], int.Parse(_configuration["Email:SmtpPort"]))
        {
            Credentials = new NetworkCredential(_configuration["Email:SenderEmail"], _configuration["Email:SenderPassword"]),
            EnableSsl = true
        };
        var mailMessage = new MailMessage{
            From = new MailAddress(_configuration["Email:SenderEmail"], _configuration["Email:SenderName"]),
            Subject = subject,
            Body = body,
            IsBodyHtml = true
        };
        mailMessage.To.Add(toEmail);
        await client.SendMailAsync(mailMessage);
    }
}