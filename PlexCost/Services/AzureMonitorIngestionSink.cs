using System;
using System.Collections.Generic;
using System.Linq;
using Azure;
using Azure.Monitor.Ingestion;
using Serilog.Core;
using Serilog.Events;

namespace PlexCost.Services
{
    /// <summary>
    /// A Serilog sink that pushes every LogEvent into Azure Monitor via the LogsIngestionClient.
    /// </summary>
    public class AzureMonitorIngestionSink(
        LogsIngestionClient client,
        string dataCollectionRuleId,
        string streamName) : ILogEventSink
    {
        readonly LogsIngestionClient _client = client;
        readonly string _dcrId = dataCollectionRuleId;
        readonly string _streamName = streamName;

        public void Emit(LogEvent logEvent)
        {
            // Build a plain‐old‐CLR object that matches your JSON shape
            var entry = new
            {
                logEvent.Timestamp,                      // DateTimeOffset
                Level = logEvent.Level.ToString(),               // "Debug", etc.
                MessageTemplate = logEvent.MessageTemplate.Text,           // "Computed Totals for {User}: …"
                RenderedMessage = logEvent.RenderMessage(),                // fully substituted string
                Properties = ExtractProperties(logEvent.Properties)   // dictionary of everything
            };

            // fire-and-forget
            _ = _client
                .UploadAsync(_dcrId, _streamName, [entry])
                .ConfigureAwait(false)
                .GetAwaiter();
        }

        private static Dictionary<string, object?> ExtractProperties(
            IReadOnlyDictionary<string, LogEventPropertyValue> props)
        {
            static object? Convert(LogEventPropertyValue v)
            {
                return v switch
                {
                    ScalarValue s => s.Value,
                    SequenceValue s => s.Elements.Select(Convert).ToArray(),
                    StructureValue s => s.Properties.ToDictionary(p => p.Name, p => Convert(p.Value)),
                    DictionaryValue d => d.Elements.ToDictionary(e => e.Key.Value?.ToString() ?? "", e => Convert(e.Value)),
                    _ => v.ToString()
                };
            }

            return props.ToDictionary(kvp => kvp.Key, kvp => Convert(kvp.Value));
        }
    }
}
