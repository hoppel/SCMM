﻿using SCMM.Web.Data.Models.Domain.Currencies;

namespace SCMM.Web.Data.Models.UI.MarketStatistics
{
    public class DashboardAssetMarketValueDTO : DashboardAssetDTO
    {
        public CurrencyDTO Currency { get; set; }

        public long BuyNowPrice { get; set; }
    }
}
