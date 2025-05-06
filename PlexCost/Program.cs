using PlexCost.Configuration;
using PlexCost.Services;
using Serilog;
using Serilog.Context;
using static PlexCost.Services.LoggerService;

namespace PlexCost
{
    public class Program
    {
        /// <summary>
        /// Application entry point: loads config, then loops indefinitely
        /// fetching history, appending new records, recomputing savings,
        /// and waiting the configured interval.
        /// </summary>
        public static async Task Main()
        {
            try
            {

                var config = PlexCostConfig.FromEnvironment();

                // Initialize record tracking
                var recordService = new RecordService(config.DataJsonPath);

                // Main loop: run, then wait HoursBetweenRuns
                while (true)
                {
                    var runId = Guid.NewGuid().ToString();

                    // Ensure RunId is included in all log entries during this iteration
                    using (LogContext.PushProperty("RunId", runId))
                    {
                        LogDebug("Starting new run. RunId: {RunId}", runId);

                        try
                        {
                            // Query Tautulli API for the last 2 days of history
                            var after = DateTime.Now.AddDays(-2).ToString("yyyy-MM-dd");
                            var endpoint =
                                $"http://{config.IpAddress}:{config.Port}/api/v2?apikey={config.ApiKey}" +
                                $"&cmd=get_history&after={after}&length=10000";

                            LogInformation("Fetching Tautulli history from {Endpoint}", endpoint);
                            var history = await GetHistory.FetchHistoryAsync(endpoint);
                            LogInformation("Tautulli returned {Count} records.", history.Count);

                            // Append any new entries to data.csv
                            var (written, skipped) =
                                await recordService.AppendNewRecordsAsync(history, config.PlexToken);

                            LogInformation(
                                "Records processed. Written: {Written}, Skipped: {Skipped}",
                                written, skipped
                            );

                            // Recalculate and write savings.csv
                            SaveService.ComputeSavingsJson(
                                config.BaseSubscriptionPrice,
                                config.DataJsonPath,
                                config.SavingsJsonPath
                            );
                        }
                        catch (Exception ex)
                        {
                            // Catch-all to prevent the loop from crashing
                            LogError("Unexpected error during run {RunId}: {Message}", runId, ex.Message);
                        }

                        LogInformation("Waiting {Hours} hour(s) until next run...", config.HoursBetweenRuns);
                    }

                    // Pause before next iteration
                    await Task.Delay(TimeSpan.FromHours(config.HoursBetweenRuns));
                }
            }
            catch (Exception ex)
            {
                // If anything fails during startup, log fatally and exit
                Log.Fatal(ex, "Unhandled exception. Exiting.");
            }
            finally
            {
                // Flush and close any pending logs
                Log.CloseAndFlush();
            }
        }
    }
}
