using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Vonage;
using Vonage.Request;
using Vonage.Messaging;

namespace THYNK.Services
{
    public interface ISMSService
    {
        Task<(bool Success, string MessageId)> SendSMS(string phoneNumber, string message);
        Task<IEnumerable<(string PhoneNumber, bool Success, string MessageId)>> SendBulkSMS(
            IEnumerable<string> phoneNumbers, string message);
    }

    public class VonageSMSService : ISMSService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<VonageSMSService> _logger;
        private readonly VonageClient _client;
        private readonly string _from;

        public VonageSMSService(IConfiguration configuration, ILogger<VonageSMSService> logger)
        {
            _configuration = configuration;
            _logger = logger;
            
            var apiKey = _configuration["Vonage:ApiKey"];
            var apiSecret = _configuration["Vonage:ApiSecret"];
            _from = _configuration["Vonage:From"];
            
            var credentials = Credentials.FromApiKeyAndSecret(apiKey, apiSecret);
            _client = new VonageClient(credentials);
        }

        public async Task<(bool Success, string MessageId)> SendSMS(string phoneNumber, string message)
        {
            try
            {
                _logger.LogInformation($"Sending SMS to {phoneNumber}");
                
                // Format phone number if needed
                phoneNumber = FormatPhoneNumber(phoneNumber);
                
                var response = await _client.SmsClient.SendAnSmsAsync(new SendSmsRequest
                {
                    To = phoneNumber,
                    From = _from,
                    Text = message
                });

                var messageId = response.Messages.FirstOrDefault()?.MessageId ?? "";
                var status = response.Messages.FirstOrDefault()?.Status ?? "0";
                
                bool success = status == "0";
                
                if (success)
                {
                    _logger.LogInformation($"Successfully sent SMS to {phoneNumber}, MessageId: {messageId}");
                }
                else
                {
                    _logger.LogWarning($"Failed to send SMS to {phoneNumber}. Status: {status}, Error: {response.Messages.FirstOrDefault()?.ErrorText}");
                }
                
                return (success, messageId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error sending SMS to {phoneNumber}: {ex.Message}");
                return (false, null);
            }
        }

        public async Task<IEnumerable<(string PhoneNumber, bool Success, string MessageId)>> SendBulkSMS(
            IEnumerable<string> phoneNumbers, string message)
        {
            var results = new List<(string PhoneNumber, bool Success, string MessageId)>();
            
            foreach (var phoneNumber in phoneNumbers)
            {
                var result = await SendSMS(phoneNumber, message);
                results.Add((phoneNumber, result.Success, result.MessageId));
                
                // Add a small delay to avoid rate limiting
                await Task.Delay(100);
            }
            
            return results;
        }
        
        private string FormatPhoneNumber(string phoneNumber)
        {
            // Remove any non-digit characters
            phoneNumber = new string(phoneNumber.Where(char.IsDigit).ToArray());
            
            // Ensure number has country code (Philippines)
            if (phoneNumber.Length == 10 && phoneNumber.StartsWith("9"))
            {
                phoneNumber = "63" + phoneNumber;
            }
            else if (phoneNumber.Length == 11 && phoneNumber.StartsWith("09"))
            {
                phoneNumber = "63" + phoneNumber.Substring(1);
            }
            
            return phoneNumber;
        }
    }
} 