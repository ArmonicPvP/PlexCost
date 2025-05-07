using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PlexCost.Models
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
}
