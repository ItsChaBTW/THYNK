using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using THYNK.Models;

namespace THYNK.Services
{
    public class AlertNotificationService
    {
        private readonly ISMSService _smsService;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<AlertNotificationService> _logger;

        public AlertNotificationService(
            ISMSService smsService,
            UserManager<ApplicationUser> userManager,
            ILogger<AlertNotificationService> logger)
        {
            _smsService = smsService;
            _userManager = userManager;
            _logger = logger;
        }

        public async Task SendAlertNotifications(Alert alert)
        {
            try
            {
                // Get the users based on the alert's target area
                var users = await GetTargetedUsers(alert);
                
                if (!users.Any())
                {
                    _logger.LogWarning($"No users found for alert {alert.Id} targeting {alert.AffectedArea}");
                    return;
                }

                // Format the alert message
                string message = FormatAlertMessage(alert);
                
                // Get valid phone numbers
                var phoneNumbers = users
                    .Where(u => !string.IsNullOrEmpty(u.PhoneNumber))
                    .Select(u => u.PhoneNumber)
                    .ToList();

                if (!phoneNumbers.Any())
                {
                    _logger.LogWarning($"No phone numbers found for alert {alert.Id}");
                    return;
                }
                
                _logger.LogInformation($"Sending alert {alert.Id} to {phoneNumbers.Count} recipients");
                
                // Send messages
                var results = await _smsService.SendBulkSMS(phoneNumbers, message);
                
                // Log results
                foreach (var (phoneNumber, success, messageSid) in results)
                {
                    if (success)
                    {
                        _logger.LogInformation($"Successfully sent alert {alert.Id} to {phoneNumber}, MessageSid: {messageSid}");
                    }
                    else
                    {
                        _logger.LogWarning($"Failed to send alert {alert.Id} to {phoneNumber}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error sending alert notifications: {ex.Message}");
                throw;
            }
        }

        private async Task<List<ApplicationUser>> GetTargetedUsers(Alert alert)
        {
            // Get all community users
            var users = await _userManager.Users
                .Where(u => u.UserRole == UserRoleType.Community)
                .ToListAsync();

            // If there's no specific affected area, return all community users
            if (string.IsNullOrEmpty(alert.AffectedArea))
            {
                return users;
            }

            // Parse the affected area to filter users
            // In a real implementation, you'd need to parse the affected area string
            // to determine which regions/provinces/cities/barangays are affected
            // For now, we'll do a simple string contains check
            
            return users
                .Where(u => 
                    alert.AffectedArea.Contains(u.RegionName, StringComparison.OrdinalIgnoreCase) ||
                    alert.AffectedArea.Contains(u.ProvinceName, StringComparison.OrdinalIgnoreCase) ||
                    alert.AffectedArea.Contains(u.CityMunicipalityName, StringComparison.OrdinalIgnoreCase) ||
                    alert.AffectedArea.Contains(u.BarangayName, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        private string FormatAlertMessage(Alert alert)
        {
            // Format the message based on alert type and severity
            string prefix = alert.Severity switch
            {
                AlertSeverity.Critical => "CRITICAL ALERT: ",
                AlertSeverity.Danger => "URGENT ALERT: ",
                AlertSeverity.Warning => "ALERT: ",
                _ => "Notice: "
            };

            // Truncate message if too long
            string description = alert.Message.Length > 100
                ? alert.Message.Substring(0, 97) + "..."
                : alert.Message;

            return $"{prefix}{alert.Title} - {description}";
        }
    }
} 