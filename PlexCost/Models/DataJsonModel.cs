namespace PlexCost.Models
{
    /// <summary>
    /// One raw history record in JSON form.
    /// </summary>
    public class DataRecordJson
    {
        public string GUID { get; set; } = "";
        public long DateStopped { get; set; }
        public double MaximumPrice { get; set; }
        public double AveragePrice { get; set; }
        public List<string> SubscriptionNames { get; set; } = [];
    }

    /// <summary>
    /// All raw records for one user.
    /// </summary>
    public class UserDataJson
    {
        public string UserName { get; set; } = "";
        public List<DataRecordJson> Records { get; set; } = [];
    }
}
