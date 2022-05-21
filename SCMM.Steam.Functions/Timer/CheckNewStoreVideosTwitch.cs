﻿using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SCMM.Google.Client;
using SCMM.Shared.Data.Store.Types;
using SCMM.Steam.Data.Models.Enums;
using SCMM.Steam.Data.Store;
using System.Text.RegularExpressions;

namespace SCMM.Steam.Functions.Timer;

public class CheckNewStoreVideosTwitch
{
    private readonly SteamDbContext _db;
    private readonly GoogleClient _googleClient;
    private readonly CheckNewStoreVideosConfiguration _configuration;

    public CheckNewStoreVideosTwitch(IConfiguration configuration, SteamDbContext db, GoogleClient googleClient)
    {
        _db = db;
        _googleClient = googleClient;
        _configuration = configuration.GetSection("StoreVideos").Get<CheckNewStoreVideosConfiguration>();
    }

    [Function("Check-New-Store-Videos-Twitch")]
    public async Task Run([TimerTrigger("0 10 * * * *")] /* every hour, 10 mins past */ TimerInfo timerInfo, FunctionContext context)
    {
        var logger = context.GetLogger("Check-New-Store-Videos-Twitch");

        var steamApps = await _db.SteamApps
            .Where(x => x.Features.HasFlag(SteamAppFeatureTypes.StoreRotating))
            .Where(x => x.IsActive)
            .ToListAsync();
        if (!steamApps.Any())
        {
            return;
        }

        foreach (var app in steamApps)
        {
            var activeItemStores = await _db.SteamItemStores
                .Where(x => x.AppId == app.Id)
                .Where(x => x.Start != null && x.End == null)
                .OrderByDescending(x => x.Start)
                .ToListAsync();

            foreach (var itemStore in activeItemStores)
            {
                var media = new Dictionary<DateTimeOffset, string>();
                foreach (var channel in _configuration.Channels.Where(x => x.Type == CheckNewStoreVideosConfiguration.ChannelType.Twitch))
                {
                    try
                    {
                        // TODO: If we already have a video for this channel, don't waste time checking again
                        // NOTE: We only accept one video per-channel, per-store
                        /*
                        if (itemStore.Media.ContainsKey(channel.ChannelId))
                        {
                            continue;
                        }
                        */

                        // Find the earliest video that matches our store data period.
                        logger.LogTrace($"Checking channel (id: {channel.ChannelId}) for new store videos since {itemStore.Start.Value.UtcDateTime}...");
                        // TODO: Implement this...
                        // https://github.com/TwitchLib/TwitchLib.Api
                        /*
                        var videos = await _twitchClient.ListChannelVideosAsync(channel.ChannelId);
                        var firstStoreVideo = videos
                            .Where(x => Regex.IsMatch(x.Title, channel.Query, RegexOptions.IgnoreCase))
                            .Where(x => x.PublishedAt != null && x.PublishedAt.Value.UtcDateTime >= itemStore.Start.Value.UtcDateTime && x.PublishedAt.Value.UtcDateTime <= itemStore.Start.Value.UtcDateTime.AddDays(7))
                            .OrderBy(x => x.PublishedAt.Value)
                            .FirstOrDefault();

                        if (firstStoreVideo != null)
                        {
                            media[firstStoreVideo.PublishedAt.Value] = firstStoreVideo.Id;
                        }
                        */
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, $"Failed to check channel (id: {channel.ChannelId}) for new store videos, skipping...");
                    }
                }

                var newMedia = media
                    .Where(x => !itemStore.Media.Contains(x.Value))
                    .OrderBy(x => x.Key)
                    .ToList();

                if (newMedia.Any())
                {
                    logger.LogInformation($"{newMedia.Count} new video(s) were found for store {itemStore.Start.Value.UtcDateTime}");
                    itemStore.Media = new PersistableStringCollection(
                        itemStore.Media.Union(newMedia.Select(x => x.Value))
                    );

                    _db.SaveChanges();
                    logger.LogTrace($"{itemStore.Media.Count} total video(s) are now recorded for store {itemStore.Start.Value.UtcDateTime}");
                }
                else
                {
                    logger.LogTrace($"No new videos were found for store {itemStore.Start.Value.UtcDateTime}");
                }
            }
        }
    }
}
