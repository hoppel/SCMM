using System.Text.Json.Serialization;

namespace SCMM.Market.SkinSwap.Client
{
    public class SkinSwapItemOverstock
    {
        [JsonPropertyName("count")]
        public int Count { get; set; }
    }
}
