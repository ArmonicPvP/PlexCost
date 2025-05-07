using PlexCost.Models;
using System.Text.Json;

namespace PlexCost
{
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

            var raw = root?.Response?.Data?.Data ?? [];

            // Keep only those with ≥80% watched, map to our lighter model
            return [.. raw
                .Where(r => r.Watched_status >= 0.8)
                .Select(r => new HistoryRecord
                {
                    User_id = r.User_id,
                    User = r.User ?? "",
                    Guid = r.Guid ?? "",
                    DateStopped = r.Stopped
                })];
        }
    }
}
