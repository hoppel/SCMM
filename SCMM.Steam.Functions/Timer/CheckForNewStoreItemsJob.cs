﻿using CommandQuery;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SCMM.Discord.API.Commands;
using SCMM.Shared.API.Extensions;
using SCMM.Shared.Data.Models;
using SCMM.Shared.Data.Models.Extensions;
using SCMM.Shared.Data.Store;
using SCMM.Steam.API;
using SCMM.Steam.API.Commands;
using SCMM.Steam.API.Queries;
using SCMM.Steam.Client;
using SCMM.Steam.Client.Extensions;
using SCMM.Steam.Data.Models;
using SCMM.Steam.Data.Models.Enums;
using SCMM.Steam.Data.Store;
using SCMM.Steam.Data.Store.Types;
using SteamWebAPI2.Interfaces;
using SteamWebAPI2.Utilities;
using System.Globalization;

namespace SCMM.Steam.Functions.Timer;

public class CheckForNewStoreItemsJob
{
    private readonly IConfiguration _configuration;
    private readonly SteamDbContext _db;
    private readonly ICommandProcessor _commandProcessor;
    private readonly IQueryProcessor _queryProcessor;
    private readonly SteamService _steamService;

    public CheckForNewStoreItemsJob(IConfiguration configuration, ICommandProcessor commandProcessor, IQueryProcessor queryProcessor, SteamDbContext db, SteamService steamService)
    {
        _configuration = configuration;
        _commandProcessor = commandProcessor;
        _queryProcessor = queryProcessor;
        _db = db;
        _steamService = steamService;
    }

    [Function("Check-New-Store-Items")]
    public async Task Run([TimerTrigger("0 * * * * *")] /* every minute */ TimerInfo timerInfo, FunctionContext context)
    {
        var logger = context.GetLogger("Check-New-Store-Items");

        var steamApps = await _db.SteamApps
            .Where(x => x.Features.HasFlag(SteamAppFeatureTypes.StorePersistent) || x.Features.HasFlag(SteamAppFeatureTypes.StoreRotating))
            .Where(x => x.IsActive)
            .ToListAsync();
        if (!steamApps.Any())
        {
            return;
        }

        var currencies = await _db.SteamCurrencies.ToListAsync();
        if (currencies == null)
        {
            return;
        }

        var steamWebInterfaceFactory = new SteamWebInterfaceFactory(_configuration.GetSteamConfiguration().ApplicationKey);
        var steamEconomy = steamWebInterfaceFactory.CreateSteamWebInterface<SteamEconomy>();
        foreach (var app in steamApps)
        {
            logger.LogTrace($"Checking for new store items (appId: {app.SteamId})");
            var usdCurrency = currencies.FirstOrDefault(x => x.Name == Constants.SteamCurrencyUSD);
            var response = await steamEconomy.GetAssetPricesAsync(
                uint.Parse(app.SteamId), string.Empty, Constants.SteamDefaultLanguage
            );
            if (response?.Data?.Success != true)
            {
                logger.LogError("Failed to get asset prices");
                continue;
            }

            // We want to compare the Steam item store with our most recent store
            var theirStoreItemIds = response.Data.Assets
                .Select(x => x.Name)
                .OrderBy(x => x)
                .Distinct()
                .ToList();
            var ourStoreItemIds = _db.SteamItemStores
                .Where(x => x.AppId == app.Id)
                .Where(x => x.End == null)
                .OrderByDescending(x => x.Start)
                .SelectMany(x => x.Items.Where(i => i.Item.IsAvailable).Select(i => i.Item.SteamId))
                .Distinct()
                .ToList();

            // If both stores contain the same items, then there is no need to update anything
            var storesAreTheSame = ourStoreItemIds != null && theirStoreItemIds.All(x => ourStoreItemIds.Contains(x)) && ourStoreItemIds.All(x => theirStoreItemIds.Contains(x));
            if (storesAreTheSame)
            {
                continue;
            }

            logger.LogInformation($"New store change detected! (appId: {app.SteamId})");

            // If we got here, then then item store has changed (either added or removed items)
            // Load all of our active stores and their items
            var activeItemStores = _db.SteamItemStores
                .Where(x => x.AppId == app.Id)
                .Where(x => x.End == null)
                .OrderByDescending(x => x.Start)
                .Include(x => x.Items).ThenInclude(x => x.Item)
                .Include(x => x.Items).ThenInclude(x => x.Item.Description)
                .ToList();
            var limitedItemsWereRemoved = false;
            foreach (var itemStore in activeItemStores.ToList())
            {
                var thisStoreItemIds = itemStore.Items.Select(x => x.Item.SteamId).ToList();
                var missingStoreItemIds = thisStoreItemIds.Where(x => !theirStoreItemIds.Contains(x));
                if (missingStoreItemIds.Any())
                {
                    foreach (var missingStoreItemId in missingStoreItemIds)
                    {
                        var missingStoreItem = itemStore.Items.FirstOrDefault(x => x.Item.SteamId == missingStoreItemId);
                        if (missingStoreItem != null)
                        {
                            missingStoreItem.Item.IsAvailable = false;
                            if (!missingStoreItem.Item.Description.IsPermanent)
                            {
                                limitedItemsWereRemoved = true;
                            }
                        }
                    }
                }
                if (itemStore.Start != null && itemStore.Items.Any(x => !x.Item.IsAvailable) && limitedItemsWereRemoved)
                {
                    itemStore.End = DateTimeOffset.UtcNow;
                    activeItemStores.Remove(itemStore);
                }
            }

            // Ensure that an active "general" and "limited" item store exists
            var permanentItemStore = activeItemStores.FirstOrDefault(x => x.Start == null);
            if (permanentItemStore == null && app.Features.HasFlag(SteamAppFeatureTypes.StorePersistent))
            {
                permanentItemStore = new SteamItemStore()
                {
                    App = app,
                    AppId = app.Id,
                    Name = "General"
                };
            }
            var limitedItemStore = activeItemStores.FirstOrDefault(x => x.Start != null);
            if ((limitedItemStore == null || limitedItemsWereRemoved) && app.Features.HasFlag(SteamAppFeatureTypes.StoreRotating))
            {
                limitedItemStore = new SteamItemStore()
                {
                    App = app,
                    AppId = app.Id,
                    Start = DateTimeOffset.UtcNow
                };
            }

            // Check if there are any new items to be added to the stores
            var newPermanentStoreItems = new List<SteamStoreItemItemStore>();
            var newLimitedStoreItems = new List<SteamStoreItemItemStore>();
            foreach (var asset in response.Data.Assets)
            {
                // Ensure that the item is available in the database (create them if missing)
                var storeItem = await _steamService.AddOrUpdateStoreItemAndMarkAsAvailable(
                    app, asset, usdCurrency, DateTimeOffset.Now
                );
                if (storeItem == null)
                {
                    continue;
                }

                // Ensure that the item is linked to the store
                var itemStore = (storeItem.Description.IsPermanent || !app.Features.HasFlag(SteamAppFeatureTypes.StoreRotating)) ? permanentItemStore : limitedItemStore;
                if (!storeItem.Stores.Any(x => x.StoreId == itemStore.Id) && itemStore != null)
                {
                    var prices = _steamService.ParseStoreItemPriceTable(asset.Prices);
                    var storeItemLink = new SteamStoreItemItemStore()
                    {
                        Store = itemStore,
                        Item = storeItem,
                        Currency = usdCurrency,
                        CurrencyId = usdCurrency.Id,
                        Price = prices.FirstOrDefault(x => x.Key == usdCurrency.Name).Value,
                        Prices = new PersistablePriceDictionary(prices),
                        IsPriceVerified = true
                    };
                    storeItem.Stores.Add(storeItemLink);
                    itemStore.Items.Add(storeItemLink);
                    if (itemStore == permanentItemStore)
                    {
                        newPermanentStoreItems.Add(storeItemLink);
                    }
                    else if (itemStore == limitedItemStore)
                    {
                        newLimitedStoreItems.Add(storeItemLink);
                    }
                }

                // Update the store items "latest price"
                storeItem.UpdateLatestPrice();
            }

            // Regenerate store thumbnails (if items have changed)
            if (newPermanentStoreItems.Any() && permanentItemStore != null)
            {
                if (permanentItemStore.IsTransient)
                {
                    _db.SteamItemStores.Add(permanentItemStore);
                }
                if (permanentItemStore.Items.Any())
                {
                    await RegenerateStoreItemsThumbnailImage(logger, app, permanentItemStore);
                }
            }
            if (newLimitedStoreItems.Any() && limitedItemStore != null)
            {
                if (limitedItemStore.IsTransient)
                {
                    _db.SteamItemStores.Add(limitedItemStore);
                }
                if (limitedItemStore.Items.Any())
                {
                    await RegenerateStoreItemsThumbnailImage(logger, app, limitedItemStore);
                }
            }

            _db.SaveChanges();

            // Send out a broadcast about any "new" items that weren't already in our store
            if (newPermanentStoreItems.Any())
            {
                logger.LogInformation($"New permanent store items detected!");
                await BroadcastNewStoreItemsNotification(logger, app, permanentItemStore, newPermanentStoreItems, currencies);
            }
            if (newLimitedStoreItems.Any())
            {
                logger.LogInformation($"New limited store items detected!");
                await BroadcastNewStoreItemsNotification(logger, app, limitedItemStore, newLimitedStoreItems, currencies);
            }

            // TODO: Wait 1min, then trigger CheckForNewMarketItemsJob
        }
    }

    private async Task<string> RegenerateStoreItemsThumbnailImage(ILogger logger, SteamApp app, SteamItemStore store)
    {
        try
        {
            var itemImageSources = store.Items
                .Select(x => x.Item)
                .Where(x => x?.Description != null)
                .Select(x => new ImageSource()
                {
                    ImageUrl = x.Description.IconUrl,
                    ImageData = x.Description.Icon?.Data,
                })
                .ToList();

            var thumbnailImage = await _queryProcessor.ProcessAsync(new GetImageMosaicRequest()
            {
                ImageSources = itemImageSources,
                ImageSize = 128,
                ImageColumns = 3
            });

            if (thumbnailImage != null)
            {
                store.ItemsThumbnailUrl = (
                    await _commandProcessor.ProcessWithResultAsync(new UploadImageToBlobStorageRequest()
                    {
                        Name = $"{app.SteamId}-store-items-thumbnail-{Uri.EscapeDataString(store.Start?.Ticks.ToString() ?? store.Name?.ToLower())}",
                        MimeType = thumbnailImage.MimeType,
                        Data = thumbnailImage.Data,
                        ExpiresOn = null, // never
                        Overwrite = true
                    })
                )?.ImageUrl ?? store.ItemsThumbnailUrl;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to generate store item thumbnail image");
        }

        return store.ItemsThumbnailUrl;
    }

    private async Task BroadcastNewStoreItemsNotification(ILogger logger, SteamApp app, SteamItemStore store, IEnumerable<SteamStoreItemItemStore> newStoreItems, IEnumerable<SteamCurrency> currencies)
    {
        newStoreItems = newStoreItems?.OrderBy(x => x.Item?.Description?.Name);
        if (newStoreItems?.Any() != true)
        {
            return;
        }

        var guilds = _db.DiscordGuilds.Include(x => x.Configurations).ToList();
        foreach (var guild in guilds)
        {
            try
            {
                if (!bool.Parse(guild.Get(DiscordConfiguration.AlertsStore, Boolean.TrueString).Value))
                {
                    continue;
                }

                var guildChannels = guild.List(DiscordConfiguration.AlertChannel).Value?.Union(new[] {
                    "announcement", "store", "skin", app.Name, "general", "chat", "bot"
                });

                var filteredCurrencies = currencies;
                var guildCurrencies = guild.List(DiscordConfiguration.Currency).Value;
                if (guildCurrencies?.Any() == true)
                {
                    filteredCurrencies = currencies.Where(x => guildCurrencies.Contains(x.Name)).ToList();
                }
                else
                {
                    filteredCurrencies = currencies.Where(x => x.Name == Constants.SteamCurrencyUSD).ToList();
                }

                var storeId = store.Start != null
                    ? store.Start.Value.UtcDateTime.AddMinutes(1).ToString(Constants.SCMMStoreIdDateFormat)
                    : store.Name.ToLower();

                var storeName = store.Start != null
                    ? $"{store.Start.Value.ToString("yyyy MMMM d")}{store.Start.Value.GetDaySuffix()}"
                    : store.Name;

                await _commandProcessor.ProcessAsync(new SendDiscordMessageRequest()
                {
                    GuidId = ulong.Parse(guild.DiscordId),
                    ChannelPatterns = guildChannels?.ToArray(),
                    Message = null,
                    Title = $"{app.Name} Store - {storeName}",
                    Description = $"{newStoreItems.Count()} new item(s) have been added to the store.",
                    Fields = newStoreItems.ToDictionary(
                        x => x.Item?.Description?.Name,
                        x => GetStoreItemPriceList(x, filteredCurrencies)
                    ),
                    FieldsInline = true,
                    Url = $"{_configuration.GetWebsiteUrl()}/store/{storeId}",
                    ThumbnailUrl = app.IconUrl,
                    ImageUrl = store.ItemsThumbnailUrl,
                    Colour = UInt32.Parse(app.PrimaryColor.Replace("#", ""), NumberStyles.HexNumber)
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Failed to send new store item notification to guild (id: {guild.Id})");
                continue;
            }
        }
    }

    private string GetStoreItemPriceList(SteamStoreItemItemStore storeItem, IEnumerable<SteamCurrency> currencies)
    {
        var prices = new List<string>();
        foreach (var currency in currencies.OrderBy(x => x.Name))
        {
            var price = storeItem.Prices.FirstOrDefault(x => x.Key == currency.Name);
            if (price.Value > 0)
            {
                var priceString = currency.ToPriceString(price.Value)?.Trim();
                if (!string.IsNullOrEmpty(priceString))
                {
                    prices.Add(priceString);
                }
            }
        }

        return string.Join("  •  ", prices).Trim(' ', '•');
    }
}
