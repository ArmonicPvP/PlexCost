using PlexCost.Models;
using System.Text;
using System.Text.Json;
using static PlexCost.Services.LoggerService;

namespace PlexCost.Services
{
    /// <summary>
    /// Reads data.json, computes per-user/month savings and minimal subscriptions, and writes savings.json.
    /// </summary>
    public class SaveService
    {
        public static void ComputeSavingsJson(
            double baseSubscriptionPrice,
            string dataJsonPath,
            string savingsJsonPath)
        {
            // 1) Load all records from data.json
            if (!File.Exists(dataJsonPath))
            {
                LogWarning("data.json not found; emitting empty savings JSON.");
                File.WriteAllText(savingsJsonPath, "{}");

                return;
            }

            Dictionary<int, UserDataJson>? allData;
            try
            {
                var json = File.ReadAllText(dataJsonPath, Encoding.UTF8);
                var jsonOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                allData = JsonSerializer.Deserialize<Dictionary<int, UserDataJson>>(json, jsonOpts)
                                 ?? new Dictionary<int, UserDataJson>();
            }
            catch (Exception ex)
            {
                LogError("Failed to parse data.json: {Error}", ex.Message);
                allData = new Dictionary<int, UserDataJson>();
            }

            // 2) Re-aggregate into monthly groups
            var monthlyOldTotals = new Dictionary<(int userId, string user, int year, int month), (double maxSum, double avgSum)>();
            var monthlySubs = new Dictionary<(int userId, string user, int year, int month), List<HashSet<string>>>();

            foreach (var kvp in allData)
            {
                int userId = kvp.Key;
                string userName = kvp.Value.UserName;

                foreach (var rec in kvp.Value.Records)
                {
                    // Convert Unix timestamp to UTC DateTime
                    var dt = DateTimeOffset.FromUnixTimeSeconds(rec.DateStopped).UtcDateTime;
                    var key = (userId, userName, dt.Year, dt.Month);

                    // 2a) accumulate historical sums
                    if (!monthlyOldTotals.ContainsKey(key))
                        monthlyOldTotals[key] = (0, 0);

                    var (sumMax, sumAvg) = monthlyOldTotals[key];
                    monthlyOldTotals[key] = (sumMax + rec.MaximumPrice, sumAvg + rec.AveragePrice);

                    // 2b) collect each month's subscription sets
                    if (!monthlySubs.ContainsKey(key))
                        monthlySubs[key] = new List<HashSet<string>>();

                    var platformSet = new HashSet<string>(
                        rec.SubscriptionNames ?? new List<string>(),
                           StringComparer.OrdinalIgnoreCase
                    );


                    monthlySubs[key].Add(platformSet);
                }
            }

            // 3) Compute set-cover and costs per (user,year,month)
            var monthlyResults = new Dictionary<(int, string, int, int),
                                               (double maxSum, double avgSum, double subCost, HashSet<string> chosen)>();

            foreach (var kvp in monthlySubs)
            {
                var (uId, uName, yr, mo) = kvp.Key;
                var lists = kvp.Value;

                LogDebug(
                    "Evaluating minimum subscription coverage for {User} during {Month}/{Year} with {Count} items",
                    uName, mo, yr, lists.Count
                );

                var (needed, chosen) = ComputeMinimalSetCover(lists);

                LogDebug(
                   "{User} for {Month}/{Year} needs minimum {Needed} subscriptions - {Subscriptions}",
                   uName,
                   mo,
                   yr,
                   needed,
                   string.Join(";", chosen)
               );

                var subCost = needed * baseSubscriptionPrice;



                monthlyOldTotals.TryGetValue(kvp.Key, out var oldSums);
                monthlyResults[kvp.Key] = (oldSums.maxSum, oldSums.avgSum, subCost, chosen);
            }

            // 4) Pivot into per-user JSON output
            var output = new Dictionary<int, UserSavingsJson>();
            foreach (var kvp in monthlyResults)
            {
                var (uId, uName, yr, mo) = kvp.Key;
                var (mSum, aSum, cCost, set) = kvp.Value;

                if (!output.ContainsKey(uId))
                    output[uId] = new UserSavingsJson { UserName = uName };

                var monthDto = new MonthlySavingsJson
                {
                    Year = yr,
                    Month = mo,
                    MaximumSavings = Math.Round(mSum, 2),
                    AverageSavings = Math.Round(aSum, 2),
                    SubscriptionCosts = Math.Round(cCost, 2),
                    Subscriptions = set.OrderBy(x => x).ToList()
                };


                LogDebug("Adding MonthlySavingsJson for User {UserId} ({UserName}) — Year: {Year}, Month: {Month}, Max: {Max}, Avg: {Avg}, Cost: {Cost}, Subscriptions: [{Subs}]",
                    uId,
                    uName,
                    monthDto.Year,
                    monthDto.Month,
                    monthDto.MaximumSavings,
                    monthDto.AverageSavings,
                    monthDto.SubscriptionCosts,
                    string.Join(", ", monthDto.Subscriptions)
                );

                output[uId].MonthlySavings.Add(monthDto);



            }

            foreach (var user in output.Values)
            {
                user.Totals = new TotalSavingsJson
                {
                    TotalMaximumSavings = Math.Round(user.MonthlySavings.Sum(m => m.MaximumSavings), 2),
                    TotalAverageSavings = Math.Round(user.MonthlySavings.Sum(m => m.AverageSavings), 2),
                    TotalSubscriptionCosts = Math.Round(user.MonthlySavings.Sum(m => m.SubscriptionCosts), 2),
                };

                LogDebug("Computed Totals for {User}: Max={Max}, Avg={Avg}, Cost={Cost}",
                    user.UserName,
                    user.Totals.TotalMaximumSavings,
                    user.Totals.TotalAverageSavings,
                    user.Totals.TotalSubscriptionCosts
                );
            }

            // 5) Serialize & write savings.json
            try
            {
                var opts = new JsonSerializerOptions { WriteIndented = true };
                var outJson = JsonSerializer.Serialize(output, opts);
                File.WriteAllText(savingsJsonPath, outJson, Encoding.UTF8);
                LogInformation("Wrote savings JSON to {Path}", savingsJsonPath);
            }
            catch (Exception ex)
            {
                LogError("Failed to write savings.json: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Greedy set-cover: pick the fewest platforms covering all records.
        /// </summary>
        private static (int Count, HashSet<string> Platforms)
            ComputeMinimalSetCover(List<HashSet<string>> allContentPlatforms)
        {
            int n = allContentPlatforms.Count;
            if (n == 0)
            {
                LogDebug("No records => zero subscriptions needed.");
                return (0, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            }

            var coverage = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < n; i++)
            {
                foreach (var plat in allContentPlatforms[i])
                {
                    if (!coverage.ContainsKey(plat))
                        coverage[plat] = new HashSet<int>();
                    coverage[plat].Add(i);
                }
            }

            var uncovered = new HashSet<int>(Enumerable.Range(0, n));
            var chosen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            while (uncovered.Count > 0 && coverage.Count > 0)
            {
                var best = coverage
                    .OrderByDescending(kvp => kvp.Value.Count(idx => uncovered.Contains(idx)))
                    .First();

                var newly = best.Value.Where(idx => uncovered.Contains(idx)).ToList();
                if (!newly.Any()) break;

                chosen.Add(best.Key);
                foreach (var idx in newly) uncovered.Remove(idx);
                coverage.Remove(best.Key);
            }

            return (chosen.Count, chosen);
        }
    }
}
