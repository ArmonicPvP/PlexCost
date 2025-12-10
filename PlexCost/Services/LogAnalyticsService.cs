using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Azure;
using Azure.Identity;
using Azure.Monitor.Ingestion;
using PlexCost.Configuration;
using PlexCost.Models;

namespace PlexCost.Services
{
    public static class LogAnalyticsService
    {
        private static LogsIngestionClient? _client;
        private static string? _dcrId;
        private static string? _streamName;
        private static bool _configured;

        public static void Configure(PlexCostConfigModel cfg)
        {
            if (_configured)
                return;

            if (string.IsNullOrWhiteSpace(cfg.LogAnalyticsEndpoint)
                || string.IsNullOrWhiteSpace(cfg.LogAnalyticsDataCollectionRuleId)
                || string.IsNullOrWhiteSpace(cfg.LogAnalyticsStreamName))
            {
                // Optional feature; skip initialization when not fully configured.
                return;
            }

            _client = new LogsIngestionClient(
                new Uri(cfg.LogAnalyticsEndpoint),
                new DefaultAzureCredential()
            );
            _dcrId = cfg.LogAnalyticsDataCollectionRuleId;
            _streamName = cfg.LogAnalyticsStreamName;
            _configured = true;
        }

        public static async Task SendLogsAsync(IEnumerable<LogAnalyticsRecord> records)
        {
            if (!_configured || _client is null || _dcrId is null || _streamName is null)
                return;

            var entries = new List<object>();
            foreach (var rec in records)
            {
                entries.Add(new
                {
                    rec.Timestamp,
                    rec.Level,
                    rec.MessageTemplate,
                    rec.RenderedMessage,
                    rec.Properties
                });
            }

            Response resp = await _client
                .UploadAsync(_dcrId, _streamName, entries)
                .ConfigureAwait(false);

            if (resp.IsError)
                throw new ApplicationException(
                    $"Upload failed: {resp.Status} {resp.ReasonPhrase}"
                );
        }
    }
}
