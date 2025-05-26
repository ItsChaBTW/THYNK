using System;
using System.Collections.Generic;

namespace THYNK.Models
{
    public class CommunityFeedViewModel
    {
        public List<FeedItem> Items { get; set; }
    }

    public class FeedItem
    {
        public int Id { get; set; }
        public string Type { get; set; }
        public string Title { get; set; }
        public string Message { get; set; }
        public DateTime DatePosted { get; set; }
        public ApplicationUser User { get; set; }
        public string ImageUrl { get; set; }
        public string Location { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public AlertSeverity? Severity { get; set; }
        public string BackgroundStyle { get; set; }
        public string IconStyle { get; set; }
        public string IssuedBy { get; set; }
    }
} 