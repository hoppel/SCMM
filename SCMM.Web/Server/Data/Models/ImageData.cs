﻿namespace SCMM.Web.Server.Data.Models.Steam
{
    public class ImageData : Entity
    {
        public string Source { get; set; }

        public string MimeType { get; set; }

        public byte[] Data { get; set; }
    }
}
