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
        private static readonly LogsIngestionClient _client;
        private static readonly string _dcrId;
        private static readonly string _streamName;

        static LogAnalyticsService()
        {
            var cfg = PlexCostConfig.FromEnvironment();
            _client = new LogsIngestionClient(
                                new Uri(cfg.LogAnalyticsEndpoint),
                                new DefaultAzureCredential()
                            );
            _dcrId = cfg.LogAnalyticsDataCollectionRuleId;
            _streamName = cfg.LogAnalyticsStreamName;
        }

        public static async Task SendLogsAsync(IEnumerable<LogAnalyticsRecord> records)
        {
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
