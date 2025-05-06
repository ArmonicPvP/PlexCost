using System.Text.Json;

namespace PlexCost
{
    /// <summary>
    /// Represents the raw JSON returned by Tautulli’s history API,
    /// where 'Stopped' is a Unix timestamp.
    /// </summary>
    public class HistoryRawRecord
    {
        public int User_id { get; set; }
        public string? User { get; set; }
        public string? Guid { get; set; }
        public double Watched_status { get; set; }
        public long Stopped { get; set; }  // Unix timestamp
    }

    /// <summary>
    /// Filtered model we write to CSV, carrying only the fields we need.
    /// </summary>
    public class HistoryRecord
    {
        public int User_id { get; set; }
        public string User { get; set; } = "";
        public string Guid { get; set; } = "";
        public long DateStopped { get; set; }  // Same Unix timestamp
    }

    // JSON response wrappers
    public class HistoryRoot { public HistoryResponse? Response { get; set; } }
    public class HistoryResponse
    {
        public string? Result { get; set; }
        public string? Message { get; set; }
        public HistoryData? Data { get; set; }
    }
    public class HistoryData { public List<HistoryRawRecord>? Data { get; set; } }

    /// <summary>
    /// Fetches history from the Tautulli endpoint, deserializes it,
    /// and returns only those with watched_status ≥ 0.8.
    /// </summary>
    public static class GetHistory
    {
        public static async Task<List<HistoryRecord>> FetchHistoryAsync(string url)
        {
            using var httpClient = new HttpClient();

            var response = await httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var root = JsonSerializer.Deserialize<HistoryRoot>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );

            var raw = root?.Response?.Data?.Data ?? new List<HistoryRawRecord>();

            // Keep only those with ≥80% watched, map to our lighter model
            return raw
                .Where(r => r.Watched_status >= 0.8)
                .Select(r => new HistoryRecord
                {
                    User_id = r.User_id,
                    User = r.User ?? "",
                    Guid = r.Guid ?? "",
                    DateStopped = r.Stopped
                })
                .ToList();
        }
    }
}
