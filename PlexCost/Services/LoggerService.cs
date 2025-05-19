using Azure.Identity;
using Azure.Monitor.Ingestion;
using Microsoft.Extensions.Logging;
using PlexCost.Configuration;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Json;

namespace PlexCost.Services
{
    public static class LoggerService
    {
        static LoggerService()
        {
            var cfg = PlexCostConfig.FromEnvironment();

            var logLevel = cfg.Debug ? LogEventLevel.Debug : LogEventLevel.Information;

            var ingestionClient = new LogsIngestionClient(
                new Uri(cfg.LogAnalyticsEndpoint),
                new DefaultAzureCredential()
            );

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Is(logLevel)
                .Enrich.FromLogContext()
                .WriteTo.Console(new JsonFormatter(renderMessage: true))
                .WriteTo.File(
                    new JsonFormatter(renderMessage: true),
                    path: cfg.LogsJsonPath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7
                )

                .WriteTo.Sink(
                    new AzureMonitorIngestionSink(
                        ingestionClient,
                        cfg.LogAnalyticsDataCollectionRuleId,
                        cfg.LogAnalyticsStreamName
                    )
                )
                .CreateLogger();

            var factory = LoggerFactory.Create(builder =>
            {
                builder.ClearProviders();
                builder.AddSerilog(dispose: true);
            });
        }

        public static void LogDebug(string template, params object[] args) => Log.Debug(template, args);
        public static void LogInformation(string template, params object[] args) => Log.Information(template, args);
        public static void LogWarning(string template, params object[] args) => Log.Warning(template, args);
        public static void LogError(string template, params object[] args) => Log.Error(template, args);
        public static void LogCritical(string template, params object[] args) => Log.Fatal(template, args);
    }
}
