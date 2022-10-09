﻿using System.Text.Json;

namespace SCMM.Market.SkinsMonkey.Client
{
    public class SkinsMonkeyWebClient : Shared.Client.WebClient
    {
        private const string ApiUri = "https://skinsmonkey.com/api/public/v1/";

        private readonly SkinsMonkeyConfiguration _configuration;

        public SkinsMonkeyWebClient(SkinsMonkeyConfiguration configuration)
        {
            _configuration = configuration;
        }

        public async Task<IEnumerable<SkinsMonkeyItem>> GetItemPricesAsync(string appId)
        {
            using (var client = BuildWebApiHttpClient(_configuration.ApiKey))
            {
                var url = $"{ApiUri}price/{Uri.EscapeDataString(appId)}";
                var response = await client.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var textJson = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<IEnumerable<SkinsMonkeyItem>>(textJson);
            }
        }
    }
}
