﻿using SCMM.Shared.Abstractions.WebProxies;
using System.Text.Json;

namespace SCMM.Webshare.Client
{
    public class WebshareWebClient : Shared.Client.WebClient, IWebProxyManagementService
    {
        private const string BaseUri = "https://proxy.webshare.io/api";
        private const int MaxPageSize = 25;

        private readonly WebshareConfiguration _configuration;

        public WebshareWebClient(WebshareConfiguration configuration)
        {
            _configuration = configuration;
        }

        public async Task<IEnumerable<IWebProxyDetails>> ListWebProxiesAsync()
        {
            using (var client = BuildWebShareClient())
            {
                var results = new List<IWebProxyDetails>();
                var url = $"{BaseUri}/v2/proxy/list/?mode=direct&page={1}&page_size={MaxPageSize}";

                while (url != null)
                {
                    var response = await client.GetAsync(url);
                    response.EnsureSuccessStatusCode();

                    var textJson = await response.Content.ReadAsStringAsync();
                    var responseJson = JsonSerializer.Deserialize<WebshareListProxiesResponseJson>(textJson);
                    if (responseJson.Results != null)
                    {
                        results.AddRange(responseJson.Results);
                    }

                    url = !string.IsNullOrEmpty(responseJson.Next)
                        ? responseJson.Next
                        : null;
                }

                return results;
            }
        }

        private HttpClient BuildWebShareClient() => BuildWebApiHttpClient(
            authHeaderName: "Authorization",
            authHeaderFormat: "Token {0}",
            authKey: _configuration.ApiKey
        );
    }
}
