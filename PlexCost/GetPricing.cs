using System.Net.Http.Headers;
using System.Text.Json;
using static PlexCost.Services.LoggerService;

namespace PlexCost
{
    // JSON mappings for the pricing API response
    public class PricingRoot
    {
        public PricingMediaContainer? MediaContainer { get; set; }
    }

    public class PricingMediaContainer
    {
        public List<PricingAvailability>? Availability { get; set; }
    }

    public class PricingAvailability
    {
        public string? Platform { get; set; }
        public string? OfferType { get; set; }
        public double? Price { get; set; }
    }

    /// <summary>
    /// Summarizes the maximum & average buy/rent prices,
    /// plus a semicolon-delimited list of subscription platforms.
    /// </summary>
    public class PricingSummary
    {
        public double? MaximumPrice { get; set; }
        public double? AveragePrice { get; set; }
        public string? SubscriptionNames { get; set; }
    }

    /// <summary>
    /// Calls Plex’s pricing endpoint for a given GUID,
    /// with retry/back-off on HTTP 429 responses.
    /// </summary>
    public static class GetPricing
    {
        private static readonly HttpClient HttpClient = new();

        public static async Task<PricingSummary> FetchPricingSummaryAsync(
            string guid,
            string plexToken)
        {
            // Extract numeric ID from the GUID
            var lastSlash = guid.LastIndexOf('/');
            var metadataId = lastSlash >= 0 ? guid[(lastSlash + 1)..] : guid;

            // Build the Plex Discover URL with token
            var url =
                $"https://discover.provider.plex.tv/library/metadata/{metadataId}/availabilities" +
                "?includeAirings=1&includePlexRentals=1&includePlexPurchases=0" +
                $"&X-Plex-Token={plexToken}";

            const int MaxAttempts = 4;
            int[] backoffSeconds = { 60, 90, 120 };

            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                try
                {
                    var req = new HttpRequestMessage(HttpMethod.Get, url);
                    req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                    var resp = await HttpClient.SendAsync(req);

                    if (resp.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        // If we still have retries left, wait then retry
                        if (attempt < MaxAttempts)
                        {
                            var delay = backoffSeconds[attempt - 1];
                            LogWarning(
                                "429 for {Url}, backing off {Delay}s (attempt {Attempt}/{Max})",
                                url, delay, attempt, MaxAttempts);
                            await Task.Delay(TimeSpan.FromSeconds(delay));
                            continue;
                        }

                        // Out of retries → abort
                        LogError(
                            "Received 429 {Max} times for {Url}. Aborting.",
                            MaxAttempts, url);
                        throw new InvalidOperationException("Too many 429 responses.");
                    }

                    if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        // No offers found → return empty summary
                        LogWarning("404 Not Found for URL: {Url}", url);
                        return new PricingSummary();
                    }

                    resp.EnsureSuccessStatusCode();

                    var json = await resp.Content.ReadAsStringAsync();
                    var root = JsonSerializer.Deserialize<PricingRoot>(
                        json,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                    );

                    var offers = root?.MediaContainer?.Availability ?? new List<PricingAvailability>();

                    // Collect buy/rent prices for max & avg calculations
                    var buyRentPrices = offers
                        .Where(o => o.Price.HasValue &&
                                    (string.Equals(o.OfferType, "buy", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(o.OfferType, "rent", StringComparison.OrdinalIgnoreCase)))
                        .Select(o => o.Price!.Value)
                        .ToList();

                    var maxPrice = buyRentPrices.Any() ? buyRentPrices.Max() : (double?)null;
                    var avgPrice = buyRentPrices.Any() ? buyRentPrices.Average() : (double?)null;

                    // Gather all subscription platform names
                    var subs = offers
                        .Where(o => string.Equals(o.OfferType, "subscription", StringComparison.OrdinalIgnoreCase) &&
                                    !string.IsNullOrEmpty(o.Platform))
                        .Select(o => o.Platform!.Trim())
                        .Distinct()
                        .ToList();

                    var subNames = subs.Any() ? string.Join(";", subs) : null;

                    return new PricingSummary
                    {
                        MaximumPrice = maxPrice,
                        AveragePrice = avgPrice,
                        SubscriptionNames = subNames
                    };
                }
                catch (HttpRequestException ex)
                {
                    // Network error → return empty summary
                    LogError("HTTP request to {Url} failed: {Msg}", url, ex.Message);
                    return new PricingSummary();
                }
            }

            // Fallback in case logic above fails
            throw new InvalidOperationException("Unexpected pricing fetch logic error.");
        }
    }
}
