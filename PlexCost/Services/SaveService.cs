using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using PlexCost.Models;
using static PlexCost.Services.LoggerService;

namespace PlexCost.Services
{
    /// <summary>
    /// Reads data.csv, computes per-user/month savings and minimal subscriptions, and writes savings.json.
    /// </summary>
    public class SaveService
    {
        public static void ComputeSavingsJson(
            double baseSubscriptionPrice,
            string dataCsvPath,
            string savingsJsonPath)
        {
            // 1) Aggregate raw data per (user,year,month)
            var monthlyOldTotals = new Dictionary<(int userId, string user, int year, int month), (double maxSum, double avgSum)>();
            var monthlySubs = new Dictionary<(int userId, string user, int year, int month), List<HashSet<string>>>();

            if (!File.Exists(dataCsvPath))
            {
                LogWarning("data.csv not found; emitting empty JSON.");
                File.WriteAllText(savingsJsonPath, "{}");
                return;
            }

            using var reader = new StreamReader(dataCsvPath, Encoding.UTF8);
            reader.ReadLine(); // skip header

            while (!reader.EndOfStream)
            {
                var line = reader.ReadLine();
                if (string.IsNullOrWhiteSpace(line)) continue;
                var cols = line.Split(',');
                if (cols.Length < 7) continue;
                if (!int.TryParse(cols[0], out var userId)) continue;

                var user = cols[1];
                var dateStr = cols[3];
                if (!long.TryParse(dateStr, out var unixTs)) continue;
                var dt = DateTimeOffset.FromUnixTimeSeconds(unixTs).UtcDateTime;

                var key = (userId, user, dt.Year, dt.Month);

                // 1a) accumulate CSV‐based price sums
                if (!monthlyOldTotals.ContainsKey(key))
                    monthlyOldTotals[key] = (0, 0);
                var (accMax, accAvg) = monthlyOldTotals[key];
                double maxPriceVal = double.TryParse(cols[4], out var m) ? m : 0;
                double avgPriceVal = double.TryParse(cols[5], out var a) ? a : 0;
                monthlyOldTotals[key] = (accMax + maxPriceVal, accAvg + avgPriceVal);

                // 1b) collect each record’s subscription list
                if (!monthlySubs.ContainsKey(key))
                    monthlySubs[key] = new List<HashSet<string>>();

                var subNames = cols[6];
                var platforms = string.IsNullOrWhiteSpace(subNames)
                    ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    : subNames
                        .Split(';')
                        .Select(s => s.Trim())
                        .Where(s => s.Length > 0)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                monthlySubs[key].Add(platforms);
            }

            // 2) For each month, compute minimal set-cover + cost
            //    and store both cost & chosen platforms
            var monthlyResults = new Dictionary<(int, string, int, int),
                                               (double maxSum, double avgSum, double subCost, HashSet<string> chosen)>();
            foreach (var kvp in monthlySubs)
            {
                var (uId, uName, yr, mo) = kvp.Key;
                var lists = kvp.Value;

                var (needed, chosenPlatforms) = ComputeMinimalSetCover(lists);
                var subscriptionCost = needed * baseSubscriptionPrice;

                monthlyOldTotals.TryGetValue(kvp.Key, out var oldSums);
                monthlyResults[kvp.Key] = (oldSums.maxSum, oldSums.avgSum, subscriptionCost, chosenPlatforms);
            }

            // 3) Pivot into per-user JSON model
            var output = new Dictionary<int, UserSavingsJson>();
            foreach (var kvp in monthlyResults)
            {
                var (uId, uName, yr, mo) = kvp.Key;
                var (mSum, aSum, cCost, set) = kvp.Value;

                if (!output.ContainsKey(uId))
                    output[uId] = new UserSavingsJson { UserName = uName };

                output[uId].MonthlySavings.Add(new MonthlySavingsJson
                {
                    Year = yr,
                    Month = mo,
                    MaximumSavings = Math.Round(mSum, 2),
                    AverageSavings = Math.Round(aSum, 2),
                    SubscriptionCosts = Math.Round(cCost, 2),
                    Subscriptions = set.OrderBy(s => s).ToList()
                });
            }

            // 4) Serialize & write
            var opts = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(output, opts);
            File.WriteAllText(savingsJsonPath, json);

            LogInformation("Wrote savings JSON (with subscriptions) to {Path}", savingsJsonPath);
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

            // map platform → which record‐indices it covers
            var coverage = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < n; i++)
                foreach (var plat in allContentPlatforms[i])
                {
                    if (!coverage.ContainsKey(plat))
                        coverage[plat] = new HashSet<int>();
                    coverage[plat].Add(i);
                }

            var uncovered = new HashSet<int>(Enumerable.Range(0, n));
            var chosen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            while (uncovered.Count > 0 && coverage.Count > 0)
            {
                // choose platform covering most uncovered
                var best = coverage
                    .OrderByDescending(kvp => kvp.Value.Count(idx => uncovered.Contains(idx)))
                    .First();

                var newly = best.Value.Where(idx => uncovered.Contains(idx)).ToList();
                if (!newly.Any()) break;

                chosen.Add(best.Key);
                foreach (var idx in newly)
                    uncovered.Remove(idx);
                coverage.Remove(best.Key);
            }

            return (chosen.Count, chosen);
        }
    }
}
