﻿using System;
using System.Collections.Generic;
using SCMM.Steam.Data.Models.Domain.Currencies;

namespace SCMM.Web.Data.Models.Domain.DTOs.MarketItems
{
    public class MarketItemDetailDTO
    {
        public Guid Id { get; set; }

        public string Name { get; set; }

        public string IconUrl { get; set; }

        public string BackgroundColour { get; set; }

        public string ForegroundColour { get; set; }

        public CurrencyDTO Currency { get; set; }

        public int? Subscriptions { get; set; }

        public int? Favourited { get; set; }

        public int? Views { get; set; }

        public IDictionary<string, string> Tags { get; set; }
    }
}