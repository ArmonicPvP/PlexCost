namespace PlexCost.Models
{
    /// <summary>
    /// Represents savings for a single year/month.
    /// </summary>
    public class MonthlySavingsJson
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public double MaximumSavings { get; set; }
        public double AverageSavings { get; set; }
        public double SubscriptionCosts { get; set; }
        public List<string> Subscriptions { get; set; } = new();
    }

    /// <summary>
    /// Container for a single user's name and their per-month savings.
    /// </summary>
    public class UserSavingsJson
    {
        public string UserName { get; set; } = "";
        public List<MonthlySavingsJson> MonthlySavings { get; set; } = new();
    }
}
