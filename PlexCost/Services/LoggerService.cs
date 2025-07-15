using Azure.Identity;
using Azure.Monitor.Ingestion;
using Microsoft.Extensions.Logging;
using PlexCost.Configuration;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;
using Serilog.Formatting.Json;

namespace PlexCost.Services
{
    public static class LoggerService
    {
        static LoggerService()
        {
            var cfg = PlexCostConfig.FromEnvironment();
            var logLevel = cfg.Debug ? LogEventLevel.Debug : LogEventLevel.Information;

            // Azure Monitor sink
            var ingestionClient = new LogsIngestionClient(
                new Uri(cfg.LogAnalyticsEndpoint),
                new DefaultAzureCredential()
            );

            // Base Serilog config
            var loggerConfig = new LoggerConfiguration()
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
                );

            // If the user set a Discord log channel, wire up our DiscordLogSink
            if (cfg.DiscordLogChannelId > 0)
            {
                loggerConfig = loggerConfig.WriteTo.Sink(
                    new DiscordLogSink(cfg.DiscordBotToken, cfg.DiscordLogChannelId)
                );
            }

            // Build the global logger
            Log.Logger = loggerConfig.CreateLogger();

            // Hook Serilog into Microsoft.Extensions.Logging
            LoggerFactory.Create(builder =>
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

    /// <summary>
    /// A Serilog sink that forwards warnings+ to a Discord channel.
    /// </summary>
    class DiscordLogSink : ILogEventSink
    {
        private readonly DiscordService _discord;
        private readonly MessageTemplateTextFormatter _fmt;

        public DiscordLogSink(string botToken, ulong channelId)
        {
            // Reuse your DiscordService to send logs
            _discord = new DiscordService(botToken, "", logChannelId: channelId);
            _discord.InitializeAsync().GetAwaiter().GetResult();

            // We only care about rendering the final message body
            _fmt = new MessageTemplateTextFormatter("{Message}", null);
        }

        public void Emit(LogEvent logEvent)
        {
            // Only warnings and above
            if (logEvent.Level < LogEventLevel.Warning) return;

            using var sw = new StringWriter();
            _fmt.Format(logEvent, sw);

            // Send it off, fire‐and‐forget
            _ = _discord.SendLogAsync(
                logEvent.Level.ToString().ToUpperInvariant(),
                sw.ToString().TrimEnd()
            );
        }
    }
}
