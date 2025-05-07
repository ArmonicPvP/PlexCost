namespace PlexCost.Models
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
}
