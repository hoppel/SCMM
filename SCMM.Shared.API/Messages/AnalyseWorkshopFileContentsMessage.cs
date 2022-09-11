﻿using SCMM.Azure.ServiceBus;
using SCMM.Azure.ServiceBus.Attributes;

namespace SCMM.Steam.API.Messages
{
    [Queue(Name = "Analyse-Workshop-File-Contents")]
    [DuplicateDetection(DiscardDuplicatesSentWithinLastMinutes = 1440 /* 1 day */)]
    public class AnalyseWorkshopFileContentsMessage : Message
    {
        public override string Id => $"{BlobName}";

        public string BlobName { get; set; }

        public bool Force { get; set; }
    }
}
