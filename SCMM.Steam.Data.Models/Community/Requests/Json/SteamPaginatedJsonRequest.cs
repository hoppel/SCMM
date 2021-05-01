﻿namespace SCMM.Steam.Data.Models.Community.Requests.Json
{
    public abstract class SteamPaginatedJsonRequest : SteamRequest
    {
        public int Start { get; set; }

        public int Count { get; set; }

        public string Language { get; set; }

        public string CurrencyId { get; set; }

        public bool NoRender { get; set; } = true;
    }
}