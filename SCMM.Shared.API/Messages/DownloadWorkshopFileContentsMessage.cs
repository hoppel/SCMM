﻿using SCMM.Azure.ServiceBus;
using SCMM.Azure.ServiceBus.Attributes;

namespace SCMM.Shared.API.Messages
{
    [Queue(Name = "Download-Workshop-File-Contents")]
    [DuplicateDetection(DiscardDuplicatesSentWithinLastMinutes = 1440 /* 1 day */)]
    public class DownloadWorkshopFileContentsMessage : Message
    {
        public override string Id => $"{AppId}/{PublishedFileId}";

        public ulong AppId { get; set; }

        public ulong PublishedFileId { get; set; }

        public bool Force { get; set; }
    }
}
