﻿using CommandQuery;
using Microsoft.EntityFrameworkCore;
using SCMM.Shared.Data.Store;
using SCMM.Steam.Client;
using SCMM.Steam.Data.Models.Community.Requests.Blob;
using SCMM.Steam.Data.Store;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace SCMM.Steam.API.Commands
{
    public class FetchAndCreateImageDataRequest : ICommand<FetchAndCreateImageDataResponse>
    {
        public string Url { get; set; }

        public DateTimeOffset? ExpiresOn { get; set; } = null;

        /// <summary>
        /// If true, we'll recycle existing image data the same source url exists in the database already
        /// </summary>
        public bool UseExisting { get; set; } = true;
    }

    public class FetchAndCreateImageDataResponse
    {
        public ImageData Image { get; set; }
    }

    public class FetchAndCreateImageData : ICommandHandler<FetchAndCreateImageDataRequest, FetchAndCreateImageDataResponse>
    {
        private readonly SteamDbContext _db;
        private readonly SteamCommunityWebClient _communityClient;

        public FetchAndCreateImageData(SteamDbContext db, SteamCommunityWebClient communityClient)
        {
            _db = db;
            _communityClient = communityClient;
        }

        public async Task<FetchAndCreateImageDataResponse> HandleAsync(FetchAndCreateImageDataRequest request)
        {
            // If we have already fetched this image source before, return the existing copy
            if (request.UseExisting)
            {
                var existingImageData = await _db.ImageData.FirstOrDefaultAsync(x => x.Source == request.Url);
                if (existingImageData != null)
                {
                    return new FetchAndCreateImageDataResponse
                    {
                        Image = existingImageData
                    };
                }
            }

            // Fetch the image from its source
            var imageResponse = await _communityClient.GetBinary(new SteamBlobRequest(request.Url));
            if (imageResponse == null)
            {
                return null;
            }

            var imageData = new ImageData()
            {
                Source = request.Url,
                MimeType = imageResponse.Item2,
                Data = imageResponse.Item1,
                ExpiresOn = request.ExpiresOn
            };

            // Save the new image data to the database
            _db.ImageData.Add(imageData);

            return new FetchAndCreateImageDataResponse
            {
                Image = imageData
            };
        }
    }
}
