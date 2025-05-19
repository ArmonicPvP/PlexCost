using PlexCost.Models;
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
        /// <summary>
        /// Creates a new config instance by reading environment variables,
        /// applying defaults, then validating required values.
        /// </summary>
        public static PlexCostConfigModel FromEnvironment()
        {

            Console.WriteLine("Reading environment variables for configuration...");

            var config = new PlexCostConfigModel();

            // HOURS_BETWEEN_RUNS → default 6 hours
            string? hrsEnv = GetEnvironmentVariable("HOURS_BETWEEN_RUNS");
            config.HoursBetweenRuns = int.TryParse(hrsEnv, out var hrs) ? hrs : 6;

            // BASE_SUBSCRIPTION_PRICE → default $13.99
            string? priceEnv = GetEnvironmentVariable("BASE_SUBSCRIPTION_PRICE");
            config.BaseSubscriptionPrice = double.TryParse(priceEnv, out var price) ? price : 13.99;

            // Paths for CSV files, with fallbacks
            config.DataJsonPath = GetEnvironmentVariable("DATA_JSON_PATH") ?? "data.json";
            config.SavingsJsonPath = GetEnvironmentVariable("SAVINGS_JSON_PATH") ?? "savings.json";
            config.LogsJsonPath = GetEnvironmentVariable("LOGS_JSON_PATH") ?? "logs/plexcost-.json";

            // Network settings
            config.IpAddress = GetEnvironmentVariable("IP_ADDRESS") ?? "127.0.0.1";
            config.Port = GetEnvironmentVariable("PORT") ?? "80";

            // API credentials
            config.ApiKey = GetEnvironmentVariable("API_KEY") ?? "";
            config.PlexToken = GetEnvironmentVariable("PLEX_TOKEN") ?? "";
            config.DiscordBotToken = GetEnvironmentVariable("DISCORD_BOT_TOKEN") ?? "";

            // Log Analytics
            config.LogAnalyticsEndpoint = GetEnvironmentVariable("LOG_ANALYTICS_ENDPOINT") ?? "";
            config.LogAnalyticsDataCollectionRuleId = GetEnvironmentVariable("LOG_ANALYTICS_DCR_ID") ?? "";
            config.LogAnalyticsStreamName = GetEnvironmentVariable("LOG_ANALYTICS_STREAM_NAME") ?? "PlexCostLogs";

            // Debugging logs
            config.Debug = bool.TryParse(GetEnvironmentVariable("DEBUG"), out var debug) && debug;

            // Ensure required settings are set
            return config;
        }

        /// <summary>
        /// Ensures that all critical environment variables are present;
        /// logs a fatal error and throws if anything is missing.
        /// </summary>
        public static void Validate(PlexCostConfigModel config)
        {
            // Dictionary of variable names → their current values
            var required = new Dictionary<string, string?>
            {
                ["API_KEY"] = config.ApiKey,
                ["PLEX_TOKEN"] = config.PlexToken,
                ["DISCORD_BOT_TOKEN"] = config.DiscordBotToken,
                ["LOG_ANALYTICS_ENDPOINT"] = config.LogAnalyticsEndpoint,
                ["LOG_ANALYTICS_DCR_ID"] = config.LogAnalyticsDataCollectionRuleId,
                ["LOG_ANALYTICS_STREAM_NAME"] = config.LogAnalyticsStreamName,
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
