namespace PlexCost.Models
{
    public class LogAnalyticsRecord
    {
        // match your "Timestamp" field
        public DateTimeOffset Timestamp { get; set; }

        public string Level { get; set; } = "";

        public string MessageTemplate { get; set; } = "";

        public string RenderedMessage { get; set; } = "";

        // Serilog Properties object
        public IDictionary<string, object>? Properties { get; set; }
    }
}
