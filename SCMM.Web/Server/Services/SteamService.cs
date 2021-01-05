﻿using CommandQuery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SCMM.Steam.Client;
using SCMM.Steam.Shared;
using SCMM.Steam.Shared.Community.Models;
using SCMM.Steam.Shared.Community.Requests.Blob;
using SCMM.Steam.Shared.Community.Requests.Json;
using SCMM.Steam.Shared.Community.Responses.Json;
using SCMM.Web.Server.Data;
using SCMM.Web.Server.Data.Models.Steam;
using SCMM.Web.Server.Data.Types;
using SCMM.Web.Server.Extensions;
using SCMM.Web.Server.Services.Commands;
using SCMM.Web.Shared;
using SCMM.Web.Shared.Domain.DTOs.Currencies;
using SCMM.Web.Shared.Domain.DTOs.InventoryItems;
using Steam.Models;
using Steam.Models.SteamEconomy;
using SteamWebAPI2.Interfaces;
using SteamWebAPI2.Utilities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SCMM.Web.Server.Services
{
    public class SteamService
    {
        private readonly TimeSpan DefaultCachePeriod = TimeSpan.FromHours(6);

        private readonly ScmmDbContext _db;
        private readonly SteamConfiguration _cfg;
        private readonly SteamCommunityClient _communityClient;
        private readonly SteamCurrencyService _currencyService;
        private readonly SteamLanguageService _languageService;
        private readonly ICommandProcessor _commandProcessor;
        private readonly IQueryProcessor _queryProcessor;


        public SteamService(ScmmDbContext db, IConfiguration cfg, SteamCommunityClient communityClient, SteamCurrencyService currencyService, SteamLanguageService languageService, ICommandProcessor commandProcessor, IQueryProcessor queryProcessor)
        {
            _db = db;
            _cfg = cfg?.GetSteamConfiguration();
            _communityClient = communityClient;
            _currencyService = currencyService;
            _languageService = languageService;
            _commandProcessor = commandProcessor;
            _queryProcessor = queryProcessor;
        }

        public DateTimeOffset? GetStoreNextUpdateExpectedOn()
        {
            var lastItemAcceptedOn = _db.SteamAssetWorkshopFiles
                .AsNoTracking()
                .Where(x => x.AcceptedOn != null)
                .GroupBy(x => 1)
                .Select(x => x.Max(y => y.AcceptedOn))
                .FirstOrDefault()?.UtcDateTime;
            if (lastItemAcceptedOn == null)
            {
                return null;
            }

            // Store normally updates every thursday or friday around 9pm (UTC time)
            var nextStoreUpdateUtc = (lastItemAcceptedOn.Value.Date + new TimeSpan(21, 0, 0));
            do
            {
                nextStoreUpdateUtc = nextStoreUpdateUtc.AddDays(1);
            } while (nextStoreUpdateUtc.DayOfWeek != DayOfWeek.Thursday);

            // If the expected store date is still in the past, assume it is a day late
            // NOTE: Has a tolerance of 3hrs from the expected time
            while ((nextStoreUpdateUtc + TimeSpan.FromHours(3)) <= DateTime.UtcNow)
            {
                nextStoreUpdateUtc = nextStoreUpdateUtc.AddDays(1);
            }

            return new DateTimeOffset(nextStoreUpdateUtc, TimeZoneInfo.Utc.BaseUtcOffset);
        }

        public async Task<ProfileInventoryTotalsDTO> GetProfileInventoryTotal(string steamId, CurrencyDetailedDTO currency)
        {
            // Load the profile
            var inventory = _db.SteamProfiles
                .AsNoTracking()
                .Where(x => x.SteamId == steamId || x.ProfileId == steamId)
                .Select(x => new
                {
                    Profile = x,
                    TotalItems = x.InventoryItems.Count,
                    LastUpdatedOn = x.LastUpdatedInventoryOn
                })
                .FirstOrDefault();

            // Load the profile inventory
            var profileInventoryItems = _db.SteamProfileInventoryItems
                .Where(x => x.Profile.SteamId == steamId || x.Profile.ProfileId == steamId)
                // TODO: Use MarketItem or StoreItem for value calculation
                .Where(x => x.Description.MarketItem != null)
                .Select(x => new
                {
                    Quantity = x.Quantity,
                    BuyPrice = x.BuyPrice,
                    ExchangeRateMultiplier = (x.Currency != null ? x.Currency.ExchangeRateMultiplier : 0),
                    MarketItemLast1hrValue = x.Description.MarketItem.Last1hrValue,
                    MarketItemLast24hrValue = x.Description.MarketItem.Last24hrValue,
                    MarketItemResellPrice = x.Description.MarketItem.ResellPrice,
                    MarketItemResellTax = x.Description.MarketItem.ResellTax,
                    MarketItemExchangeRateMultiplier = (x.Description.MarketItem.Currency != null ? x.Description.MarketItem.Currency.ExchangeRateMultiplier : 0)
                })
                .ToList();

            if (!profileInventoryItems.Any())
            {
                return null;
            }

            var profileInventory = new
            {
                TotalItems = profileInventoryItems
                    .Sum(x => x.Quantity),
                TotalInvested = profileInventoryItems
                    .Where(x => x.BuyPrice != null && x.BuyPrice != 0 && x.ExchangeRateMultiplier != 0)
                    .Sum(x => (x.BuyPrice / x.ExchangeRateMultiplier) * x.Quantity),
                TotalMarketValueLast1hr = profileInventoryItems
                    .Where(x => x.MarketItemLast1hrValue != 0 && x.MarketItemExchangeRateMultiplier != 0)
                    .Sum(x => (x.MarketItemLast1hrValue / x.MarketItemExchangeRateMultiplier) * x.Quantity),
                TotalMarketValueLast24hr = profileInventoryItems
                    .Where(x => x.MarketItemLast24hrValue != 0 && x.MarketItemExchangeRateMultiplier != 0)
                    .Sum(x => (x.MarketItemLast24hrValue / x.MarketItemExchangeRateMultiplier) * x.Quantity),
                TotalResellValue = profileInventoryItems
                    .Where(x => x.MarketItemResellPrice != 0 && x.MarketItemExchangeRateMultiplier != 0)
                    .Sum(x => (x.MarketItemResellPrice / x.MarketItemExchangeRateMultiplier) * x.Quantity),
                TotalResellTax = profileInventoryItems
                    .Where(x => x.MarketItemResellTax != 0 && x.MarketItemExchangeRateMultiplier != 0)
                    .Sum(x => (x.MarketItemResellTax / x.MarketItemExchangeRateMultiplier) * x.Quantity)
            };

            return new ProfileInventoryTotalsDTO()
            {
                TotalItems = profileInventory.TotalItems,
                TotalInvested = currency.CalculateExchange(profileInventory.TotalInvested ?? 0),
                TotalMarketValue = currency.CalculateExchange(profileInventory.TotalMarketValueLast1hr),
                TotalMarket24hrMovement = currency.CalculateExchange(profileInventory.TotalMarketValueLast1hr - profileInventory.TotalMarketValueLast24hr),
                TotalResellValue = currency.CalculateExchange(profileInventory.TotalResellValue),
                TotalResellTax = currency.CalculateExchange(profileInventory.TotalResellTax),
                TotalResellProfit = (
                    currency.CalculateExchange(profileInventory.TotalResellValue - profileInventory.TotalResellTax) - currency.CalculateExchange(profileInventory.TotalInvested ?? 0)
                )
            };
        }

        public async Task FetchProfileInventory(string steamId)
        {
            var profile = await _db.SteamProfiles
                .Include(x => x.InventoryItems).ThenInclude(x => x.App)
                .Include(x => x.InventoryItems).ThenInclude(x => x.Description)
                .Include(x => x.InventoryItems).ThenInclude(x => x.Currency)
                .FirstOrDefaultAsync(x => x.SteamId == steamId || x.ProfileId == steamId);
            if (profile == null)
            {
                return;
            }

            var language = _db.SteamLanguages
                .FirstOrDefault(x => x.IsDefault);
            if (language == null)
            {
                return;
            }

            var apps = _db.SteamApps.ToList();
            if (!apps.Any())
            {
                return;
            }

            foreach (var app in apps)
            {
                // Fetch assets
                var inventory = await _communityClient.GetInventoryPaginated(new SteamInventoryPaginatedJsonRequest()
                {
                    AppId = app.SteamId,
                    SteamId = profile.SteamId,
                    Start = 1,
                    Count = SteamInventoryPaginatedJsonRequest.MaxPageSize,
                    NoRender = true
                });
                if (inventory?.Success != true)
                {
                    // Inventory is probably private
                    continue;
                }
                if (inventory?.Assets?.Any() != true)
                {
                    // Inventory doesn't have any items for this app
                    continue;
                }

                // Add assets
                var missingAssets = inventory.Assets
                    .Where(x => !profile.InventoryItems.Any(y => y.SteamId == x.AssetId))
                    .ToList();
                foreach (var asset in missingAssets)
                {
                    var assetDescription = await AddOrUpdateAssetDescription(app, language, UInt64.Parse(asset.ClassId));
                    if (assetDescription == null)
                    {
                        continue;
                    }
                    var inventoryItem = new SteamProfileInventoryItem()
                    {
                        SteamId = asset.AssetId,
                        Profile = profile,
                        ProfileId = profile.Id,
                        App = app,
                        AppId = app.Id,
                        Description = assetDescription,
                        DescriptionId = assetDescription.Id,
                        Quantity = asset.Amount
                    };

                    profile.InventoryItems.Add(inventoryItem);
                }

                // Update assets
                foreach (var asset in inventory.Assets)
                {
                    var existingAsset = profile.InventoryItems.FirstOrDefault(x => x.SteamId == asset.AssetId);
                    if (existingAsset != null)
                    {
                        existingAsset.Quantity = asset.Amount;
                    }
                }

                // Remove assets
                var removedAssets = profile.InventoryItems
                    .Where(x => !inventory.Assets.Any(y => y.AssetId == x.SteamId))
                    .ToList();
                foreach (var asset in removedAssets)
                {
                    profile.InventoryItems.Remove(asset);
                }

                profile.LastUpdatedInventoryOn = DateTimeOffset.Now;
                _db.SaveChanges();
            }
        }

        public Data.Models.Steam.SteamAssetFilter AddOrUpdateAppAssetFilter(SteamApp app, Steam.Shared.Community.Models.SteamAssetFilter filter)
        {
            var existingFilter = app.Filters.FirstOrDefault(x => x.SteamId == filter.Name);
            if (existingFilter != null)
            {
                // Nothing to update
                return existingFilter;
            }

            var newFilter = new Data.Models.Steam.SteamAssetFilter()
            {
                SteamId = filter.Name,
                Name = filter.Localized_Name,
                Options = new Data.Types.PersistableStringDictionary(
                    filter.Tags.ToDictionary(
                        x => x.Key,
                        x => x.Value.Localized_Name
                    )
                )
            };

            app.Filters.Add(newFilter);
            _db.SaveChanges();
            return newFilter;
        }

        public async Task<Data.Models.Steam.SteamAssetDescription> UpdateAssetDescription(Data.Models.Steam.SteamAssetDescription assetDescription, AssetClassInfoModel assetClass)
        {
            // Update tags
            if (assetClass.Tags != null)
            {
                foreach (var tag in assetClass.Tags)
                {
                    if (!assetDescription.Tags.ContainsKey(tag.Category))
                    {
                        assetDescription.Tags[tag.Category] = tag.Name;
                    }
                }
            }

            // Update name and description
            if (String.IsNullOrEmpty(assetDescription.Name) && !String.IsNullOrEmpty(assetClass.Name))
            {
                assetDescription.Name = assetClass.Name;
            }

            //if (String.IsNullOrEmpty(assetDescription.Description) && assetClass.Descriptions?.Count > 0)
            //{
            //    assetDescription.Description = assetClass.Descriptions.FirstOrDefault()?.Value;
            //}

            // Update icons
            if (String.IsNullOrEmpty(assetDescription.IconUrl) && !String.IsNullOrEmpty(assetClass.IconUrl?.ToString()))
            {
                assetDescription.IconUrl = assetClass.IconUrl.ToString();
            }
            if (assetDescription.IconId == null && !String.IsNullOrEmpty(assetDescription.IconUrl))
            {
                var fetchAndCreateImageData = await _commandProcessor.ProcessWithResultAsync(new FetchAndCreateImageDataRequest()
                {
                    Url = assetDescription.IconUrl,
                    UseExisting = true
                });
                if (fetchAndCreateImageData?.Image != null)
                {
                    assetDescription.Icon = fetchAndCreateImageData.Image;
                }
            }
            if (String.IsNullOrEmpty(assetDescription.IconLargeUrl) && !String.IsNullOrEmpty(assetClass.IconUrlLarge?.ToString()))
            {
                assetDescription.IconLargeUrl = assetClass.IconUrlLarge.ToString();
            }
            if (assetDescription.IconLargeId == null && !String.IsNullOrEmpty(assetDescription.IconLargeUrl))
            {
                var fetchAndCreateImageData = await _commandProcessor.ProcessWithResultAsync(new FetchAndCreateImageDataRequest()
                {
                    Url = assetDescription.IconLargeUrl,
                    UseExisting = true
                });
                if (fetchAndCreateImageData?.Image != null)
                {
                    assetDescription.IconLarge = fetchAndCreateImageData.Image;
                }
            }

            //switch (assetClass.Type)
            //{
            //    case "Workshop Item": break;
            //}

            switch (assetClass.Tradable)
            {
                case "1": assetDescription.Flags |= Shared.Data.Models.Steam.SteamAssetDescriptionFlags.Tradable; break;
                case "0": assetDescription.Flags &= ~Shared.Data.Models.Steam.SteamAssetDescriptionFlags.Tradable; break;
            }
            switch (assetClass.Marketable)
            {
                case "1": assetDescription.Flags |= Shared.Data.Models.Steam.SteamAssetDescriptionFlags.Marketable; break;
                case "0": assetDescription.Flags &= ~Shared.Data.Models.Steam.SteamAssetDescriptionFlags.Marketable; break;
            }
            switch (assetClass.Commodity)
            {
                case "1": assetDescription.Flags |= Shared.Data.Models.Steam.SteamAssetDescriptionFlags.Commodity; break;
                case "0": assetDescription.Flags &= ~Shared.Data.Models.Steam.SteamAssetDescriptionFlags.Commodity; break;
            }

            // Update last checked on
            assetDescription.LastCheckedOn = DateTimeOffset.Now;
            return assetDescription;
        }

        public async Task<Data.Models.Steam.SteamAssetDescription> UpdateAssetDescription(Data.Models.Steam.SteamAssetDescription assetDescription, PublishedFileDetailsModel publishedFile, bool updateSubscriptionGraph = false)
        {
            // Update asset description tags
            if (assetDescription != null && publishedFile.Tags != null)
            {
                foreach (var tag in publishedFile.Tags.Where(x => !SteamConstants.SteamIgnoredWorkshopTags.Any(y => x == y)))
                {
                    var tagTrimmed = tag.Replace(" ", String.Empty).Trim();
                    var tagKey = $"{SteamConstants.SteamAssetTagWorkshop}.{Char.ToLowerInvariant(tagTrimmed[0]) + tagTrimmed.Substring(1)}";
                    if (!assetDescription.Tags.ContainsKey(tagKey))
                    {
                        assetDescription.Tags[tagKey] = tag;
                    }
                }
            }

            // Update workshop infomation
            var workshopFile = assetDescription?.WorkshopFile;
            if (workshopFile != null)
            {
                // Update images
                if (String.IsNullOrEmpty(workshopFile.ImageUrl) && !String.IsNullOrEmpty(publishedFile.PreviewUrl?.ToString()))
                {
                    workshopFile.ImageUrl = publishedFile.PreviewUrl.ToString();
                }
                if (workshopFile.ImageId == null && !String.IsNullOrEmpty(workshopFile.ImageUrl))
                {
                    var fetchAndCreateImageData = await _commandProcessor.ProcessWithResultAsync(new FetchAndCreateImageDataRequest()
                    {
                        Url = workshopFile.ImageUrl,
                        UseExisting = true
                    });
                    if (fetchAndCreateImageData?.Image != null)
                    {
                        workshopFile.Image = fetchAndCreateImageData.Image;
                    }
                }

                // Update creator
                if (workshopFile.CreatorId == null && publishedFile.Creator > 0)
                {
                    var fetchAndCreateProfile = await _commandProcessor.ProcessWithResultAsync(new FetchAndCreateSteamProfileRequest()
                    {
                        Id = publishedFile.Creator.ToString()
                    });
                    if (fetchAndCreateProfile?.Profile != null)
                    {
                        workshopFile.Creator = fetchAndCreateProfile.Profile;
                        if (!assetDescription.Tags.ContainsKey(SteamConstants.SteamAssetTagCreator))
                        {
                            assetDescription.Tags[SteamConstants.SteamAssetTagCreator] = fetchAndCreateProfile.Profile.Name;
                        }
                    }
                }

                // Update timestamps
                if (publishedFile.TimeCreated > DateTime.MinValue)
                {
                    workshopFile.CreatedOn = publishedFile.TimeCreated;
                }
                if (publishedFile.TimeUpdated > DateTime.MinValue)
                {
                    workshopFile.UpdatedOn = publishedFile.TimeUpdated;
                }
                if (workshopFile.AcceptedOn > DateTimeOffset.MinValue)
                {
                    if (!assetDescription.Tags.ContainsKey(SteamConstants.SteamAssetTagAcceptedYear))
                    {
                        if (workshopFile.AcceptedOn.HasValue)
                        {
                            var culture = CultureInfo.InvariantCulture;
                            var acceptedOn = workshopFile.AcceptedOn.Value.UtcDateTime;
                            int acceptedOnWeek = culture.Calendar.GetWeekOfYear(acceptedOn, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Sunday);
                            assetDescription.Tags[SteamConstants.SteamAssetTagAcceptedYear] = acceptedOn.ToString("yyyy");
                            assetDescription.Tags[SteamConstants.SteamAssetTagAcceptedWeek] = $"Week {acceptedOnWeek}";
                        }
                    }
                }

                // Update subscriptions, favourites, views
                workshopFile.Subscriptions = (int)Math.Max(publishedFile.LifetimeSubscriptions, publishedFile.Subscriptions);
                workshopFile.Favourited = (int)Math.Max(publishedFile.LifetimeFavorited, publishedFile.Favorited);
                workshopFile.Views = (int)publishedFile.Views;
                if (updateSubscriptionGraph)
                {
                    var utcDate = DateTime.UtcNow.Date;
                    var maxSubscriptions = workshopFile.Subscriptions;
                    if (workshopFile.SubscriptionsGraph.ContainsKey(utcDate))
                    {
                        maxSubscriptions = (int)Math.Max(maxSubscriptions, workshopFile.SubscriptionsGraph[utcDate]);
                    }
                    workshopFile.SubscriptionsGraph[utcDate] = maxSubscriptions;
                    workshopFile.SubscriptionsGraph = new Data.Types.PersistableDailyGraphDataSet(
                        workshopFile.SubscriptionsGraph
                    );
                }

                // Update flags
                if (!workshopFile.Flags.HasFlag(Shared.Data.Models.Steam.SteamAssetWorkshopFileFlags.Banned) && publishedFile.Banned)
                {
                    workshopFile.Flags |= Shared.Data.Models.Steam.SteamAssetWorkshopFileFlags.Banned;
                    workshopFile.BanReason = publishedFile.BanReason;
                }
                if (workshopFile.Flags.HasFlag(Shared.Data.Models.Steam.SteamAssetWorkshopFileFlags.Banned) && !publishedFile.Banned)
                {
                    workshopFile.Flags &= ~Shared.Data.Models.Steam.SteamAssetWorkshopFileFlags.Banned;
                    workshopFile.BanReason = null;
                }
            }

            // Update last checked on
            workshopFile.LastCheckedOn = DateTimeOffset.Now;
            return assetDescription;
        }

        public SteamStoreItemItemStore UpdateStoreItemIndex(SteamStoreItemItemStore storeItem, int storeIndex)
        {
            var utcDateTime = (DateTime.UtcNow.Date + TimeSpan.FromHours(DateTime.UtcNow.TimeOfDay.Hours));
            storeItem.Index = storeIndex;
            storeItem.IndexGraph[utcDateTime] = storeIndex;
            storeItem.IndexGraph = new Data.Types.PersistableHourlyGraphDataSet(
                storeItem.IndexGraph
            );

            return storeItem;
        }

        ///
        /// UPDATE BELOW...
        ///

        public async Task<SteamAssetWorkshopFile> AddOrUpdateAssetWorkshopFile(SteamApp app, string fileId)
        {
            var dbWorkshopFile = await _db.SteamAssetWorkshopFiles
                .Where(x => x.SteamId == fileId)
                .FirstOrDefaultAsync();

            if (dbWorkshopFile != null)
            {
                return dbWorkshopFile;
            }

            dbWorkshopFile = new SteamAssetWorkshopFile()
            {
                SteamId = fileId,
                AppId = app.Id
            };

            _db.SteamAssetWorkshopFiles.Add(dbWorkshopFile);
            _db.SaveChanges();
            return dbWorkshopFile;
        }

        public async Task<Data.Models.Steam.SteamAssetDescription> AddOrUpdateAssetDescription(SteamApp app, SteamLanguage language, ulong classId)
        {
            var dbAssetDescription = await _db.SteamAssetDescriptions
                .Where(x => x.SteamId == classId.ToString())
                .Include(x => x.WorkshopFile)
                .FirstOrDefaultAsync();

            if (dbAssetDescription != null)
            {
                return dbAssetDescription;
            }

            var steamWebInterfaceFactory = new SteamWebInterfaceFactory(_cfg.ApplicationKey);
            var steamEconomy = steamWebInterfaceFactory.CreateSteamWebInterface<SteamEconomy>();
            var response = await steamEconomy.GetAssetClassInfoAsync(
                UInt32.Parse(app.SteamId), new List<ulong>() { classId }, language.SteamId
            );
            if (response?.Data?.Success != true)
            {
                return null;
            }

            var assetDescription = response.Data.AssetClasses.FirstOrDefault(x => x.ClassId == classId);
            if (assetDescription == null)
            {
                return null;
            }

            var tags = new Dictionary<string, string>();
            var workshopFile = (SteamAssetWorkshopFile)null;
            var workshopFileId = (string)null;
            var viewWorkshopAction = assetDescription?.Actions?.FirstOrDefault(x => x.Name == SteamConstants.SteamActionViewWorkshopItem);
            if (viewWorkshopAction != null)
            {
                var workshopFileIdGroups = Regex.Match(viewWorkshopAction.Link, SteamConstants.SteamActionViewWorkshopItemRegex).Groups;
                workshopFileId = (workshopFileIdGroups.Count > 1) ? workshopFileIdGroups[1].Value : "0";
                workshopFile = await AddOrUpdateAssetWorkshopFile(app, workshopFileId);
            }

            dbAssetDescription = new Data.Models.Steam.SteamAssetDescription()
            {
                SteamId = assetDescription.ClassId.ToString(),
                AppId = app.Id,
                Name = assetDescription.MarketName,
                BackgroundColour = assetDescription.BackgroundColor.SteamColourToHexString(),
                ForegroundColour = assetDescription.NameColor.SteamColourToHexString(),
                IconUrl = new SteamEconomyImageBlobRequest(assetDescription.IconUrl),
                IconLargeUrl = new SteamEconomyImageBlobRequest(assetDescription.IconUrlLarge ?? assetDescription.IconUrl),
                WorkshopFile = workshopFile,
                Tags = new Data.Types.PersistableStringDictionary(tags)
            };

            _db.SteamAssetDescriptions.Add(dbAssetDescription);
            _db.SaveChanges();
            return dbAssetDescription;
        }

        public async Task<Data.Models.Steam.SteamAssetDescription> AddOrUpdateAssetDescription(SteamApp app, Steam.Shared.Community.Models.SteamAssetDescription assetDescription)
        {
            var dbAssetDescription = await _db.SteamAssetDescriptions
                .Where(x => x.SteamId == assetDescription.ClassId)
                .Include(x => x.WorkshopFile)
                .FirstOrDefaultAsync();

            if (dbAssetDescription != null)
            {
                return dbAssetDescription;
            }

            var tags = new Dictionary<string, string>();
            var workshopFile = (SteamAssetWorkshopFile)null;
            var workshopFileId = (string)null;
            var viewWorkshopAction = assetDescription?.Actions?.FirstOrDefault(x => x.Name == SteamConstants.SteamActionViewWorkshopItem);
            if (viewWorkshopAction != null)
            {
                var workshopFileIdGroups = Regex.Match(viewWorkshopAction.Link, SteamConstants.SteamActionViewWorkshopItemRegex).Groups;
                workshopFileId = (workshopFileIdGroups.Count > 1) ? workshopFileIdGroups[1].Value : "0";
                workshopFile = await AddOrUpdateAssetWorkshopFile(app, workshopFileId);
            }

            dbAssetDescription = new Data.Models.Steam.SteamAssetDescription()
            {
                SteamId = assetDescription.ClassId.ToString(),
                AppId = app.Id,
                Name = assetDescription.MarketName,
                BackgroundColour = assetDescription.BackgroundColor.SteamColourToHexString(),
                ForegroundColour = assetDescription.NameColor.SteamColourToHexString(),
                IconUrl = new SteamEconomyImageBlobRequest(assetDescription.IconUrl),
                IconLargeUrl = new SteamEconomyImageBlobRequest(assetDescription.IconUrlLarge ?? assetDescription.IconUrl),
                WorkshopFile = workshopFile,
                Tags = new Data.Types.PersistableStringDictionary(tags)
            };

            _db.SteamAssetDescriptions.Add(dbAssetDescription);
            _db.SaveChanges();
            return dbAssetDescription;
        }

        public async Task<SteamStoreItem> AddOrUpdateAppStoreItem(SteamApp app, SteamCurrency currency, SteamLanguage language, AssetModel asset, DateTimeOffset timeChecked)
        {
            var dbItem = await _db.SteamStoreItems
                .Include(x => x.Description)
                .Where(x => x.AppId == app.Id)
                .FirstOrDefaultAsync(x => x.SteamId == asset.Name);

            if (dbItem != null)
            {
                // Update prices
                // TODO: Move this to a seperate job (to avoid spam?)
                //if (asset.Prices != null)
                //{
                //    dbItem.Prices = new PersistablePriceDictionary(GetPriceTable(asset.Prices));
                //    dbItem.Price = dbItem.Prices.FirstOrDefault(x => x.Key == currency.Name).Value;
                //}
                return dbItem;
            }

            var assetDescription = await AddOrUpdateAssetDescription(app, language, asset.ClassId);
            if (assetDescription == null)
            {
                return null;
            }

            if (assetDescription?.WorkshopFile != null)
            {
                assetDescription.WorkshopFile.AcceptedOn = timeChecked;
            }

            var prices = GetPriceTable(asset.Prices);
            app.StoreItems.Add(dbItem = new SteamStoreItem()
            {
                SteamId = asset.Name,
                AppId = app.Id,
                Description = assetDescription,
                Prices = new PersistablePriceDictionary(prices),
                Price = prices.FirstOrDefault(x => x.Key == currency.Name).Value
            });

            return dbItem;
        }

        public IDictionary<string, long> GetPriceTable(AssetPricesModel prices)
        {
            return prices.GetType()
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .ToDictionary(
                    k => k.Name,
                    prop => (long)((uint)prop.GetValue(prices, null))
                );
        }

        public async Task<SteamMarketItem> UpdateSteamMarketItemOrders(SteamMarketItem item, Guid currencyId, SteamMarketItemOrdersHistogramJsonResponse histogram)
        {
            if (item == null || histogram?.Success != true)
            {
                return item;
            }

            // Lazy-load buy/sell order history if missing, required for recalculation
            if (item.BuyOrders?.Any() != true || item.SellOrders?.Any() != true)
            {
                item = await _db.SteamMarketItems
                    .Include(x => x.BuyOrders)
                    .Include(x => x.SellOrders)
                    .SingleOrDefaultAsync(x => x.Id == item.Id);
            }

            item.LastCheckedOrdersOn = DateTimeOffset.Now;
            item.CurrencyId = currencyId;
            item.RecalculateOrders(
                ParseSteamMarketItemOrdersFromGraph<SteamMarketItemBuyOrder>(histogram.BuyOrderGraph),
                SteamEconomyHelper.GetQuantityValueAsInt(histogram.BuyOrderCount),
                ParseSteamMarketItemOrdersFromGraph<SteamMarketItemSellOrder>(histogram.SellOrderGraph),
                SteamEconomyHelper.GetQuantityValueAsInt(histogram.SellOrderCount)
            );

            return item;
        }

        public async Task<SteamMarketItem> UpdateSteamMarketItemSalesHistory(SteamMarketItem item, Guid currencyId, SteamMarketPriceHistoryJsonResponse sales)
        {
            if (item == null || sales?.Success != true)
            {
                return item;
            }

            // Lazy-load sales history if missing, required for recalculation
            if (item.SalesHistory?.Any() != true || item.Activity?.Any() != true)
            {
                item = await _db.SteamMarketItems
                    .Include(x => x.SalesHistory)
                    .Include(x => x.Activity)
                    .SingleOrDefaultAsync(x => x.Id == item.Id);
            }

            item.LastCheckedSalesOn = DateTimeOffset.Now;
            item.CurrencyId = currencyId;
            item.RecalculateSales(
                ParseSteamMarketItemSalesFromGraph(sales.Prices)
            );

            return item;
        }

        private T[] ParseSteamMarketItemOrdersFromGraph<T>(string[][] orderGraph)
            where T : Data.Models.Steam.SteamMarketItemOrder, new()
        {
            var orders = new List<T>();
            if (orderGraph == null)
            {
                return orders.ToArray();
            }

            var totalQuantity = 0;
            for (int i = 0; i < orderGraph.Length; i++)
            {
                var price = SteamEconomyHelper.GetPriceValueAsInt(orderGraph[i][0]);
                var quantity = (SteamEconomyHelper.GetQuantityValueAsInt(orderGraph[i][1]) - totalQuantity);
                orders.Add(new T()
                {
                    Price = price,
                    Quantity = quantity,
                });
                totalQuantity += quantity;
            }

            return orders.ToArray();
        }

        private SteamMarketItemSale[] ParseSteamMarketItemSalesFromGraph(string[][] salesGraph)
        {
            var sales = new List<SteamMarketItemSale>();
            if (salesGraph == null)
            {
                return sales.ToArray();
            }

            var totalQuantity = 0;
            for (int i = 0; i < salesGraph.Length; i++)
            {
                var timeStamp = DateTime.ParseExact(salesGraph[i][0], "MMM dd yyyy HH: z", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
                var price = SteamEconomyHelper.GetPriceValueAsInt(salesGraph[i][1]);
                var quantity = SteamEconomyHelper.GetQuantityValueAsInt(salesGraph[i][2]);
                sales.Add(new SteamMarketItemSale()
                {
                    Timestamp = timeStamp,
                    Price = price,
                    Quantity = quantity,
                });
                totalQuantity += quantity;
            }

            return sales.ToArray();
        }

        ///
        /// UPDATE BELOW...
        ///

        public async Task<IEnumerable<SteamMarketItem>> FindOrAddSteamMarketItems(IEnumerable<SteamMarketSearchItem> items, SteamCurrency currency)
        {
            var dbItems = new List<SteamMarketItem>();
            foreach (var item in items)
            {
                dbItems.Add(await FindOrAddSteamMarketItem(item, currency));
            }
            return dbItems;
        }

        public async Task<SteamMarketItem> FindOrAddSteamMarketItem(SteamMarketSearchItem item, SteamCurrency currency)
        {
            if (String.IsNullOrEmpty(item?.AssetDescription.AppId))
            {
                return null;
            }

            var dbApp = _db.SteamApps.FirstOrDefault(x => x.SteamId == item.AssetDescription.AppId);
            if (dbApp == null)
            {
                return null;
            }

            var dbItem = _db.SteamMarketItems
                .Include(x => x.App)
                .Include(x => x.Currency)
                .Include(x => x.Description)
                .FirstOrDefault(x => x.Description != null && x.Description.SteamId == item.AssetDescription.ClassId);

            if (dbItem != null)
            {
                return dbItem;
            }

            var assetDescription = await AddOrUpdateAssetDescription(dbApp, item.AssetDescription);
            if (assetDescription == null)
            {
                return null;
            }

            dbApp.MarketItems.Add(dbItem = new SteamMarketItem()
            {
                App = dbApp,
                AppId = dbApp.Id,
                Description = assetDescription,
                DescriptionId = assetDescription.Id,
                Currency = currency,
                CurrencyId = currency.Id,
                Supply = item.SellListings,
                BuyNowPrice = item.SellPrice
            });

            return dbItem;
        }

        public SteamMarketItem UpdateMarketItemNameId(SteamMarketItem item, string itemNameId)
        {
            if (!String.IsNullOrEmpty(itemNameId))
            {
                item.SteamId = itemNameId;
                _db.SaveChanges();
            }

            return item;
        }
    }
}
