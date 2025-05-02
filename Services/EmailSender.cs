using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Mail;
using System.Text;
using THYNK.Models;

namespace THYNK.Services
{
    public class EmailSender : IEmailSender
    {
        private readonly EmailSettings _emailSettings;
        private readonly ILogger<EmailSender> _logger;

        public EmailSender(IOptions<EmailSettings> emailSettings, ILogger<EmailSender> logger)
        {
            _emailSettings = emailSettings.Value;
            _logger = logger;
        }

        public async Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            try
            {
                // Create client with explicit SSL settings
                var client = new SmtpClient
                {
                    Host = _emailSettings.Host,
                    Port = _emailSettings.Port,
                    EnableSsl = true, // Force SSL to be true for Gmail
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    UseDefaultCredentials = false,
                    Credentials = new NetworkCredential(_emailSettings.UserName, _emailSettings.Password),
                    Timeout = 30000 // 30 seconds timeout
                };

                // Create message
                using var mailMessage = new MailMessage
                {
                    From = new MailAddress(_emailSettings.FromEmail, _emailSettings.FromName),
                    Subject = subject,
                    Body = htmlMessage,
                    IsBodyHtml = true
                };
                
                mailMessage.To.Add(email);
                
                // Log diagnostic info
                _logger.LogInformation($"Attempting to send email to {email} via {_emailSettings.Host}:{_emailSettings.Port}");
                _logger.LogInformation($"Using sender: {_emailSettings.FromEmail}");
                
                // Send the email
                await client.SendMailAsync(mailMessage);
                
                _logger.LogInformation($"Email sent successfully to {email}");
            }
            catch (SmtpException ex)
            {
                _logger.LogError($"SMTP Error: {ex.StatusCode} - {ex.Message}");
                if (ex.InnerException != null)
                {
                    _logger.LogError($"Inner Exception: {ex.InnerException.Message}");
                }
                
                // Re-throw the exception for the caller to handle
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to send email: {ex.Message}");
                throw;
            }
        }
    }
} 