﻿using System.Text.Json.Serialization;
using SCMM.Web.Data.Models.UI.Item;

namespace SCMM.Web.Data.Models.UI.Profile.Inventory
{
    public class ProfileInventoryCollectionItemDTO : ICanBeFiltered
    {
        public ItemDescriptionDTO Item { get; set; }

        public bool IsMissing { get; set; } = true;

        [JsonIgnore]
        public string[] Filters => Item?.Filters;
    }
}