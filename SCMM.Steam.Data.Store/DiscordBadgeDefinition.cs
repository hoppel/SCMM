﻿using System;
using System.ComponentModel.DataAnnotations;
using SCMM.Shared.Data.Store;

namespace SCMM.Steam.Data.Store
{
    public class DiscordBadgeDefinition : Entity
    {
        [Required]
        public Guid GuildId { get; set; }

        public DiscordGuild Guild { get; set; }

        [Required]
        public string Name { get; set; }

        [Required]
        public Guid IconId { get; set; }

        public ImageData Icon { get; set; }
    }
}