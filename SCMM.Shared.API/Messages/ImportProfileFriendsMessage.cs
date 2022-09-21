﻿using SCMM.Azure.ServiceBus;
using SCMM.Azure.ServiceBus.Attributes;

namespace SCMM.Shared.API.Messages
{
    [Queue(Name = "Import-Profile-Friends")]
    [DuplicateDetection(DiscardDuplicatesSentWithinLastMinutes = 10080 /* 7 day */)] 
    public class ImportProfileFriendsMessage : Message
    {
        public override string Id => $"{ProfileId}";

        public string ProfileId { get; set; }
    }
}