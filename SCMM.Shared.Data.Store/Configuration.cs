﻿using System.ComponentModel.DataAnnotations;
using SCMM.Shared.Data.Store.Types;
using SCMM.Shared.Data.Store;

namespace SCMM.Shared.Data.Store
{
    public class Configuration : Entity
    {
        public Configuration()
        {
            List = new PersistableStringCollection();
        }

        [Required]
        public string Name { get; set; }

        public string Value { get; set; }

        public PersistableStringCollection List { get; set; }
    }
}