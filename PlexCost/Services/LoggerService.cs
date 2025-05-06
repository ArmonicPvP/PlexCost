using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Formatting.Json;

namespace PlexCost.Services
{
    /// <summary>
    /// Sets up Serilog as the logging backend and exposes
    /// a Microsoft.Extensions ILogger for the application.
    /// </summary>
    public static class LoggerService
    {
        // Factory used to create ILogger instances
        private static readonly ILoggerFactory _factory;

        // Single logger instance tagged with "PlexCost"
        private static readonly Microsoft.Extensions.Logging.ILogger _logger;

        static LoggerService()
        {
            // Configure Serilog to write JSON-formatted logs to console and to rolling files
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()                  // Capture Debug and above
                .Enrich.FromLogContext()               // Include context properties (e.g. RunId)
                .WriteTo.Console(new JsonFormatter(renderMessage: true))
                .WriteTo.File(
                    new JsonFormatter(renderMessage: true),
                    path: "logs/plexcost-.json",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7
                )
                .CreateLogger();

            // Wire Serilog into Microsoft.Extensions.Logging
            _factory = LoggerFactory.Create(builder =>
            {
                builder.ClearProviders();           // Remove default providers
                builder.AddSerilog(dispose: true); // Use Serilog and dispose it on shutdown
            });

            // Create a named logger for application code to use
            _logger = _factory.CreateLogger("PlexCost");
        }

        /// <summary>
        /// Provides the application’s ILogger instance.
        /// </summary>
        public static Microsoft.Extensions.Logging.ILogger Logger => _logger;

        // Convenience wrappers so callers can simply call LoggerService.LogXxx(...)
        public static void LogDebug(string msg, params object[] args) => _logger.LogDebug(msg, args);
        public static void LogInformation(string msg, params object[] args) => _logger.LogInformation(msg, args);
        public static void LogWarning(string msg, params object[] args) => _logger.LogWarning(msg, args);
        public static void LogError(string msg, params object[] args) => _logger.LogError(msg, args);
        public static void LogCritical(string msg, params object[] args) => _logger.LogCritical(msg, args);
    }
}
