using System.Collections.Generic;

namespace THYNK.Models
{
    public class AlertsViewModel
    {
        public IEnumerable<Alert> Alerts { get; set; }
        public IEnumerable<UserNotification> Notifications { get; set; }
    }
} 