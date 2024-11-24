using System.Text.Json.Serialization;

namespace SCMM.Market.SkinSwap.Client
{
    public class SkinSwapItemPrice
    {
        [JsonPropertyName("trade")]
        public long Trade { get; set; }

        [JsonPropertyName("buy")]
        public long Buy { get; set; }

        [JsonPropertyName("sell")]
        public long Sell { get; set; }
    }
}
