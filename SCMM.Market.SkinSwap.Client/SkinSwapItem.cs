using System.Text.Json.Serialization;

namespace SCMM.Market.SkinSwap.Client
{
    public class SkinSwapItem
    {
        [JsonPropertyName("appid")]
        public long AppId { get; set; }

        [JsonPropertyName("market_hash_name")]
        public string MarketHashName { get; set; }

        [JsonPropertyName("price")]
        public SkinSwapItemPrice Price { get; set; }

        [JsonPropertyName("overstock")]
        public SkinSwapItemOverstock Overstock { get; set; }
    }
}
