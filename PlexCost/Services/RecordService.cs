using PlexCost.Models;
using System.Text;
using System.Text.Json;
using static PlexCost.Services.LoggerService;

namespace PlexCost.Services
{
    /// <summary>
    /// Keeps track of which history records have already been
    /// processed, stored in data.json (per‐user). Appends only new ones.
    /// </summary>
    public class RecordService
    {
        private readonly string _dataJsonPath;
        private readonly Dictionary<int, UserDataJson> _allData = [];
        private readonly HashSet<(int userId, string guid)> _knownRecords = [];

        public RecordService(string dataJsonPath)
        {
            _dataJsonPath = dataJsonPath;
            LoadKnownRecords();
        }

        private void LoadKnownRecords()
        {
            if (!File.Exists(_dataJsonPath))
            {
                LogWarning("data.json not found; starting with empty dataset.");
                return;
            }

            try
            {
                var json = File.ReadAllText(_dataJsonPath, Encoding.UTF8);
                var dict = JsonSerializer.Deserialize<Dictionary<int, UserDataJson>>(
                    json,
                    Program.jsonOptions
                );

                if (dict != null)
                {
                    foreach (var kvp in dict)
                    {
                        _allData[kvp.Key] = kvp.Value;
                        foreach (var rec in kvp.Value.Records)
                            _knownRecords.Add((kvp.Key, rec.GUID));
                    }
                    LogInformation(
                        "Loaded {UserCount} users and {RecordCount} records from data.json.",
                        _allData.Count,
                        _knownRecords.Count
                    );
                }
            }
            catch (Exception ex)
            {
                LogError("Failed to read data.json: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Appends any new history records (no duplicates) into data.json,
        /// fetching pricing for each and returning counts.
        /// </summary>
        public async Task<(int newlyWritten, int skipped)> AppendNewRecordsAsync(
            IEnumerable<HistoryRecord> filteredHistory,
            string plexToken)
        {
            var uniqueBatch = filteredHistory
                .GroupBy(r => (r.User_id, r.Guid))
                .Select(g => g.First())
                .ToList();

            LogDebug("Unique deduped records in this batch: {Count}", uniqueBatch.Count);

            int newlyWritten = 0, skipped = 0;

            foreach (var record in uniqueBatch)
            {
                var userId = record.User_id;
                var guid = record.Guid;

                if (_knownRecords.Contains((userId, guid)))
                {
                    skipped++;
                    continue;
                }

                try
                {
                    // Fetch pricing
                    var summary = await GetPricing.FetchPricingSummaryAsync(guid, plexToken);

                    // Build JSON record
                    var dataRec = new DataRecordJson
                    {
                        GUID = guid,
                        DateStopped = record.DateStopped,
                        MaximumPrice = summary.MaximumPrice ?? 0,
                        AveragePrice = summary.AveragePrice ?? 0,
                        SubscriptionNames = summary.SubscriptionNames?
                                                .Split(';', StringSplitOptions.RemoveEmptyEntries)
                                                .Select(s => s.Trim())
                                                .ToList()
                                             ?? []
                    };

                    // Ensure user bucket exists
                    if (!_allData.ContainsKey(userId))
                        _allData[userId] = new UserDataJson { UserName = record.User };

                    _allData[userId].Records.Add(dataRec);

                    LogDebug("Wrote data.json record: {@dataRec}", dataRec);

                    _knownRecords.Add((userId, guid));
                    newlyWritten++;
                }
                catch (Exception ex)
                {
                    skipped++;
                    LogError(
                        "Skipping record User={UserId}, Guid={Guid} due to error: {Error}",
                        userId, guid, ex.Message
                    );
                }
            }

            // Write back full JSON
            try
            {
                var outJson = JsonSerializer.Serialize(_allData, Program.jsonOptions);
                File.WriteAllText(_dataJsonPath, outJson, Encoding.UTF8);

                LogDebug("Wrote updated data.json with {New} new records.", newlyWritten);
            }
            catch (Exception ex)
            {
                LogError("Failed to write data.json: {Error}", ex.Message);
            }

            return (newlyWritten, skipped);
        }
    }
}
