﻿using SCMM.Web.Data.Models.UI.Item;
using System.Collections.Generic;
using System.Linq;

namespace SCMM.Web.Data.Models.UI.Statistic
{
    public class ItemCraftingCostStatisticDTO : ItemDescriptionDTO
    {
        public long BuyNowPrice { get; set; }

        public long CraftingCost => CraftingComponents.Sum(x => x.Component.BuyNowPrice * x.Quantity);

        public IEnumerable<ItemCraftingComponentCostDTO> CraftingComponents { get; set; }
    }

    public class ItemCraftingComponentCostDTO
    {
        public ItemValueStatisticDTO Component { get; set; }

        public uint Quantity { get; set; }
    }
}