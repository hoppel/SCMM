﻿using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace SCMM.Fixer.Client
{
    public class FixerWebClient
    {
        private const string BaseUri = "https://data.fixer.io/api/";

        private readonly FixerConfiguration _cfg;

        public FixerWebClient(FixerConfiguration cfg)
        {
            _cfg = cfg;
        }

        public async Task<IDictionary<string, decimal>> GetHistoricalRatesAsync(DateTime date, string from, params string[] to)
        {
            using (var client = new HttpClient())
            {
                var url = $"{BaseUri}{date.ToString("yyyy-MM-dd")}?access_key={_cfg.ApiKey}&base={from}&symbols={String.Join(',', to)}";
                var response = await client.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var textJson = await response.Content.ReadAsStringAsync();
                var responseJson = JsonConvert.DeserializeObject<FixerHistoricalRatesResponseJson>(textJson);
                return responseJson?.Rates;
            }
        }
    }

}