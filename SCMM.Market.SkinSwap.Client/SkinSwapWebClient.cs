using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace SCMM.Market.SkinSwap.Client
{
    public class SkinSwapWebClient : Shared.Web.Client.WebClientBase
    {
        private const string ApiBaseUri = "https://api.skinswap.com/";

        private readonly SkinSwapConfiguration _configuration;

        public SkinSwapWebClient(ILogger<SkinSwapWebClient> logger, SkinSwapConfiguration configuration) : base(logger)
        {
            _configuration = configuration;
            DefaultHeaders.Add("Accept", "application/json");
        }

        public async Task<SkinSwapResponse<SkinSwapItem[]>> GetSiteInventoryAsync(string appId, int offset = 0)
        {
            using (var client = BuildWebApiHttpClient())
            {
                var url = $"{ApiBaseUri}/api/site/inventory/?offset={offset}&appid={appId}&sort=price-desc&priceMin=0&priceMax=5000000&tradehold=8";
                var response = await RetryPolicy.ExecuteAsync(() => client.GetAsync(url));
                response.EnsureSuccessStatusCode();

                var textJson = await response.Content.ReadAsStringAsync();
                if (string.IsNullOrEmpty(textJson))
                {
                    return default;
                }

                var responseJson = JsonSerializer.Deserialize<SkinSwapResponse<SkinSwapItem[]>>(textJson);
                return responseJson;
            }
        }

        [Obsolete("This API no longer works")]
        public async Task<IEnumerable<SkinSwapItem>> GetItemsAsync()
        {
            using (var client = BuildSkinsSwapClient())
            {
                var url = $"https://skinswap.com/api/v1/items";
                var response = await RetryPolicy.ExecuteAsync(() => client.GetAsync(url));
                response.EnsureSuccessStatusCode();

                var textJson = await response.Content.ReadAsStringAsync();
                if (string.IsNullOrEmpty(textJson))
                {
                    return default;
                }

                var responseJson = JsonSerializer.Deserialize<SkinSwapResponse<SkinSwapItem[]>>(textJson);
                return responseJson?.Data;
            }
        }

        private HttpClient BuildSkinsSwapClient() => BuildWebApiHttpClient(
            authHeaderName: "Authorization",
            authHeaderFormat: "Bearer {0}",
            authKey: _configuration.ApiKey
        );
    }
}
