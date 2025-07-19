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
                allData = JsonSerializer.Deserialize<Dictionary<int, UserDataJson>>(
                    json,
                    Program.jsonOptions
                ) ?? [];
            }
            catch (Exception ex)
            {
                LogError("Failed to parse data.json: {Error}", ex.Message);
                allData = [];
            }

            // 2) Group every record by (userId, userName, year, month)
            var monthlyRecords = new Dictionary<(int userId, string userName, int year, int month),
                                                 List<DataRecordJson>>();

            foreach (var kvp in allData)
            {
                int userId = kvp.Key;
                string userName = kvp.Value.UserName;

                foreach (var rec in kvp.Value.Records)
                {
                    var dt = DateTimeOffset
                        .FromUnixTimeSeconds(rec.DateStopped)
                        .UtcDateTime;
                    var key = (userId, userName, dt.Year, dt.Month);

                    if (!monthlyRecords.ContainsKey(key))
                        monthlyRecords[key] = [];

                    monthlyRecords[key].Add(rec);
                }
            }

            // 3) For each month, compute minimal subscriptions and then savings only for uncovered records
            var monthlyResults = new Dictionary<(int, string, int, int),
                                               (double maxSum, double avgSum, double subCost, HashSet<string> chosen)>();

            foreach (var kvp in monthlyRecords)
            {
                var (uId, uName, yr, mo) = kvp.Key;
                var records = kvp.Value;

                // Build list of available‐platform sets, one per record
                var platformSets = records
                    .Select(r => new HashSet<string>(
                        r.SubscriptionNames ?? [],
                        StringComparer.OrdinalIgnoreCase
                    ))
                    .ToList();

                // 3a) Compute minimal subscription set to cover as many records as possible
                var (needed, chosen) = ComputeMinimalSetCover(platformSets);
                var subCost = needed * baseSubscriptionPrice;

                // 3b) Only those records *not* covered by any chosen subscription
                var uncovered = records
                    .Where(r => !r.SubscriptionNames
                                  .Any(p => chosen.Contains(p)))
                    .ToList();

                // Sum their purchase prices into your “Max/Avg Savings”
                var maxSum = uncovered.Sum(r => r.MaximumPrice);
                var avgSum = uncovered.Sum(r => r.AveragePrice);

                LogDebug(
                    "{User} {Month}/{Year}: subCost={SubCost:F2}, totalRecords={Count}, " +
                    $"uncoveredRecords={uncovered.Count}, maxSum={maxSum:F2}, avgSum={avgSum:F2}",
                    uName, mo, yr, subCost, records.Count
                );

                monthlyResults[kvp.Key] = (maxSum, avgSum, subCost, chosen);
            }

            // 4) Pivot into per-user savings JSON
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
                    Subscriptions = [.. set.OrderBy(x => x)]
                };

                output[uId].MonthlySavings.Add(monthDto);

                LogDebug(
                    "Adding MonthlySavingsJson for {User} — {Month}/{Year}: " +
                    $"Max={monthDto.MaximumSavings}, Avg={monthDto.AverageSavings}, " +
                    $"Cost={monthDto.SubscriptionCosts}, Subscriptions=[{string.Join(",", set)}]",
                    uName, mo, yr
                );
            }

            // 5) Compute per-user totals
            foreach (var user in output.Values)
            {
                user.Totals = new TotalSavingsJson
                {
                    TotalMaximumSavings = Math.Round(user.MonthlySavings.Sum(m => m.MaximumSavings), 2),
                    TotalAverageSavings = Math.Round(user.MonthlySavings.Sum(m => m.AverageSavings), 2),
                    TotalSubscriptionCosts = Math.Round(user.MonthlySavings.Sum(m => m.SubscriptionCosts), 2),
                };

                LogDebug(
                    "Computed Totals for {User}: Max={Max}, Avg={Avg}, Cost={Cost}",
                    user.UserName,
                    user.Totals.TotalMaximumSavings,
                    user.Totals.TotalAverageSavings,
                    user.Totals.TotalSubscriptionCosts
                );
            }

            // 6) Write out the new savings.json
            try
            {
                var outJson = JsonSerializer.Serialize(output, Program.jsonOptions);
                File.WriteAllText(savingsJsonPath, outJson, Encoding.UTF8);
                LogInformation("Wrote savings JSON to {Path}", savingsJsonPath);
            }
            catch (Exception ex)
            {
                LogError("Failed to write savings.json: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Greedy set-cover: pick the fewest platforms covering the most records.
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

            // Map platform → which record indices it covers
            var coverage = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < n; i++)
            {
                foreach (var plat in allContentPlatforms[i])
                {
                    if (!coverage.ContainsKey(plat))
                        coverage[plat] = [];
                    coverage[plat].Add(i);
                }
            }

            var uncovered = new HashSet<int>(Enumerable.Range(0, n));
            var chosen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            while (uncovered.Count > 0 && coverage.Count > 0)
            {
                // pick the platform covering the most still-uncovered records
                var best = coverage
                    .OrderByDescending(kvp =>
                        kvp.Value.Count(idx => uncovered.Contains(idx)))
                    .First();

                var newlyCovered = best.Value.Where(idx => uncovered.Contains(idx)).ToList();
                if (newlyCovered.Count == 0) break;

                chosen.Add(best.Key);
                foreach (var idx in newlyCovered)
                    uncovered.Remove(idx);

                coverage.Remove(best.Key);
            }

            return (chosen.Count, chosen);
        }
    }
}
