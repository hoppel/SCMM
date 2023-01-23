﻿using System.Net;
using System.Text.Json;

namespace SCMM.Market.SkinSwap.Client
{
    public class SkinSwapWebClient : Shared.Client.WebClient
    {
        private const string BaseUri = "https://skinswap.com/api/v1/";

        public SkinSwapWebClient(IWebProxy webProxy) : base(webProxy: webProxy) { }

        public async Task<IDictionary<string, SkinSwapItem[]>> GetSiteInventoryAsync()
        {
            using (var client = BuildWebBrowserHttpClient())
            {
                var url = $"{BaseUri}site/inventory";
                var response = await client.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var textJson = await response.Content.ReadAsStringAsync();
                var responseJson = JsonSerializer.Deserialize<SkinSwapResponse<Dictionary<string, SkinSwapItem[]>>>(textJson);
                return responseJson?.Data;
            }
        }
    }
}
