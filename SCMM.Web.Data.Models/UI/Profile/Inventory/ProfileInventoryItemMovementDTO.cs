﻿using SCMM.Web.Data.Models.UI.Item;

namespace SCMM.Web.Data.Models.UI.Profile.Inventory
{
    public class ProfileInventoryItemMovementDTO : ICanBeFiltered
    {
        public ItemDescriptionDTO Item { get; set; }

        public DateTimeOffset MovementTime { get; set; }

        public long Movement { get; set; }

        public long Quantity { get; set; }

        public string[] Filters => Item?.Filters;
    }
}