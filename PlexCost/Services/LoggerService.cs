using Microsoft.Extensions.Logging;
using PlexCost.Configuration;
using PlexCost.Models;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;

namespace PlexCost.Services
{
    public static class LoggerService
    {
        private const string OutputTemplate = "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}";

        private static readonly Logger FallbackLogger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .WriteTo.Console(outputTemplate: OutputTemplate)
            .CreateLogger();

        private static bool _initialized;

        static LoggerService()
        {
            // Default to the fallback logger so early calls still reach the console.
            Log.Logger = FallbackLogger;
        }

        public static void Initialize(PlexCostConfigModel cfg)
        {
            if (_initialized) return;

            try
            {
                var logLevel = cfg.Debug ? LogEventLevel.Debug : LogEventLevel.Information;

                // Base Serilog config
                var loggerConfig = new LoggerConfiguration()
                    .MinimumLevel.Is(logLevel)
                    .Enrich.FromLogContext()
                    .WriteTo.Console(outputTemplate: OutputTemplate)
                    .WriteTo.File(
                        path: cfg.LogsPath,
                        outputTemplate: OutputTemplate,
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 7
                    );

                // Discord sink is optional; requires both a channel ID and bot token.
                if (cfg.DiscordLogChannelId > 0 && !string.IsNullOrWhiteSpace(cfg.DiscordBotToken))
                {
                    loggerConfig = loggerConfig.WriteTo.Sink(
                        new DiscordLogSink(cfg.DiscordBotToken, cfg.DiscordLogChannelId)
                    );
                }
                else if (cfg.DiscordLogChannelId > 0 || !string.IsNullOrWhiteSpace(cfg.DiscordBotToken))
                {
                    FallbackLogger.Warning("Discord logging partially configured; provide both DISCORD_BOT_TOKEN and DISCORD_LOG_CHANNEL_ID to enable the sink.");
                }

                // Build the global logger
                Log.Logger = loggerConfig.CreateLogger();
            }
            catch (Exception ex)
            {
                FallbackLogger.Error(ex, "Failed to initialize structured logging; using console fallback.");
                Log.Logger = FallbackLogger;
            }
            finally
            {
                _initialized = true;
            }

            // Hook Serilog into Microsoft.Extensions.Logging
            LoggerFactory.Create(builder =>
            {
                builder.ClearProviders();
                builder.AddSerilog(Log.Logger, dispose: true);
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
            _discord = new DiscordService(botToken, "", "", channelId);
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
