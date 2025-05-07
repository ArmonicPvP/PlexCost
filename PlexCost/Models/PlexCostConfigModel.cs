namespace PlexCost.Models
{
    public class PlexCostConfigModel
    {
        // How many hours to wait between each run of the main loop.
        public int HoursBetweenRuns { get; set; }

        // Base cost of a Plex subscription, used to compute savings.
        public double BaseSubscriptionPrice { get; set; }

        // File paths for input and output CSVs.
        public string DataJsonPath { get; set; } = "";
        public string SavingsJsonPath { get; set; } = "";
        public string LogsJsonPath { get; set; } = "";

        // Network settings for calling the Tautulli API.
        public string IpAddress { get; set; } = "";
        public string Port { get; set; } = "";

        // Credentials for API access.
        public string ApiKey { get; set; } = "";
        public string PlexToken { get; set; } = "";

        public string DiscordBotToken { get; set; } = "";
    }
}
