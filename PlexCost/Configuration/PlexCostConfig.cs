using static PlexCost.Services.LoggerService;
using static System.Environment;

namespace PlexCost.Configuration
{
    /// <summary>
    /// Holds all the configuration settings for the PlexCost application,
    /// populated from environment variables or sensible defaults.
    /// </summary>
    public class PlexCostConfig
    {
        // How many hours to wait between each run of the main loop.
        public int HoursBetweenRuns { get; set; }

        // Base cost of a Plex subscription, used to compute savings.
        public double BaseSubscriptionPrice { get; set; }

        // File paths for input and output CSVs.
        public string DataJsonPath { get; set; } = "";
        public string SavingsJsonPath { get; set; } = "";

        // Network settings for calling the Tautulli API.
        public string IpAddress { get; set; } = "";
        public string Port { get; set; } = "";

        // Credentials for API access.
        public string ApiKey { get; set; } = "";
        public string PlexToken { get; set; } = "";

        /// <summary>
        /// Creates a new config instance by reading environment variables,
        /// applying defaults, then validating required values.
        /// </summary>
        public static PlexCostConfig FromEnvironment()
        {

            Console.WriteLine("Reading environment variables for configuration...");

            var config = new PlexCostConfig();

            // HOURS_BETWEEN_RUNS → default 6 hours
            string? hrsEnv = GetEnvironmentVariable("HOURS_BETWEEN_RUNS");
            config.HoursBetweenRuns = int.TryParse(hrsEnv, out var hrs) ? hrs : 6;

            // BASE_SUBSCRIPTION_PRICE → default $13.99
            string? priceEnv = GetEnvironmentVariable("BASE_SUBSCRIPTION_PRICE");
            config.BaseSubscriptionPrice = double.TryParse(priceEnv, out var price) ? price : 13.99;

            // Paths for CSV files, with fallbacks
            config.DataJsonPath = GetEnvironmentVariable("DATA_JSON_PATH") ?? "data.json";
            config.SavingsJsonPath = GetEnvironmentVariable("SAVINGS_JSON_PATH") ?? "savings.json";

            // Network settings
            config.IpAddress = GetEnvironmentVariable("IP_ADDRESS") ?? "127.0.0.1";
            config.Port = GetEnvironmentVariable("PORT") ?? "80";

            // API credentials
            config.ApiKey = GetEnvironmentVariable("API_KEY") ?? "";
            config.PlexToken = GetEnvironmentVariable("PLEX_TOKEN") ?? "";

            // Ensure required settings are set
            config.Validate();
            return config;
        }

        /// <summary>
        /// Ensures that all critical environment variables are present;
        /// logs a fatal error and throws if anything is missing.
        /// </summary>
        private void Validate()
        {
            // Dictionary of variable names → their current values
            var required = new Dictionary<string, string?>
            {
                ["API_KEY"] = ApiKey,
                ["PLEX_TOKEN"] = PlexToken
            };

            // For each required variable, make sure it's not empty
            foreach (var kvp in required)
            {
                if (string.IsNullOrWhiteSpace(kvp.Value))
                {
                    LogCritical("{Var} environment variable is required but was missing or empty.", kvp.Key);
                    throw new InvalidOperationException($"{kvp.Key} environment variable is required.");
                }
            }

            LogInformation("Configuration validated successfully.");
        }
    }
}
