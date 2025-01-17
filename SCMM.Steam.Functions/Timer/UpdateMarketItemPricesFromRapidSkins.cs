﻿using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SCMM.Market.RapidSkins.Client;
using SCMM.Shared.Abstractions.Statistics;
using SCMM.Shared.Data.Models.Extensions;
using SCMM.Shared.Data.Models.Statistics;
using SCMM.Steam.Data.Models;
using SCMM.Steam.Data.Models.Enums;
using SCMM.Steam.Data.Models.Extensions;
using SCMM.Steam.Data.Store;
using SCMM.Steam.Data.Store.Types;
using System.Diagnostics;

namespace SCMM.Steam.Functions.Timer;

public class UpdateMarketItemPricesFromRapidSkins
{
    private const MarketType RapidSkins = MarketType.RapidSkins;

    private readonly SteamDbContext _db;
    private readonly RapidSkinsWebClient _rapidSkinsWebClient;
    private readonly IStatisticsService _statisticsService;

    public UpdateMarketItemPricesFromRapidSkins(SteamDbContext db, RapidSkinsWebClient rapidSkinsWebClient, IStatisticsService statisticsService)
    {
        _db = db;
        _rapidSkinsWebClient = rapidSkinsWebClient;
        _statisticsService = statisticsService;
    }

    [Function("Update-Market-Item-Prices-From-Rapid-Skins")]
    public async Task Run([TimerTrigger("0 7/30 * * * *")] /* every 30mins */ TimerInfo timerInfo, FunctionContext context)
    {
        if (!RapidSkins.IsEnabled())
        {
            return;
        }

        var logger = context.GetLogger("Update-Market-Item-Prices-From-Rapid-Skins");

        var appIds = RapidSkins.GetSupportedAppIds().Select(x => x.ToString()).ToArray();
        var supportedSteamApps = await _db.SteamApps
            .Where(x => appIds.Contains(x.SteamId))
            .ToListAsync();
        if (!supportedSteamApps.Any())
        {
            return;
        }

        // Prices are returned in USD by default
        var usdCurrency = _db.SteamCurrencies.FirstOrDefault(x => x.Name == Constants.SteamCurrencyUSD);
        if (usdCurrency == null)
        {
            return;
        }

        foreach (var app in supportedSteamApps)
        {
            logger.LogTrace($"Updating item price information from RapidSkins (appId: {app.SteamId})");
            await UpdateRapidSkinsMarketPricesForApp(logger, app, usdCurrency);
        }
    }

    private async Task UpdateRapidSkinsMarketPricesForApp(ILogger logger, SteamApp app, SteamCurrency usdCurrency)
    {
        var statisticsKey = String.Format(StatisticKeys.MarketStatusByAppId, app.SteamId);
        var stopwatch = new Stopwatch();
        try
        {
            stopwatch.Start();

            var rapidSkinsAppItems = new List<RapidSkinsItem>();
            var rapidSkinsPaginatedItems = (RapidSkinsPaginatedItems)null;
            var page = 1;
            do
            {
                rapidSkinsPaginatedItems = await _rapidSkinsWebClient.GetSiteInventoryAsync(app.SteamId, app.Name, page);
                if (rapidSkinsPaginatedItems.Items?.Any() == true)
                {
                    rapidSkinsAppItems.AddRange(rapidSkinsPaginatedItems.Items);
                }
                page++;
            } while (rapidSkinsPaginatedItems?.LastPage == false);

            var dbItems = await _db.SteamMarketItems
                .Where(x => x.AppId == app.Id)
                .Select(x => new
                {
                    Name = x.Description.NameHash,
                    Currency = x.Currency,
                    Item = x,
                })
                .ToListAsync();

            var rapidSkinsItemGroups = rapidSkinsAppItems.GroupBy(x => x.MarketHashName);
            foreach (var rapidSkinsItemGroup in rapidSkinsItemGroups)
            {
                var item = dbItems.FirstOrDefault(x => x.Name == rapidSkinsItemGroup.Key)?.Item;
                if (item != null)
                {
                    var price = rapidSkinsItemGroup.Where(x => x.Price != null).Min(x => x.Price.CoinAmount);
                    var supply = rapidSkinsItemGroup.Where(x => x.Stack != null).Sum(x => x.Stack.Sum(y => y.Amount));
                    item.UpdateBuyPrices(RapidSkins, new PriceWithSupply
                    {
                        Price = supply > 0 ? item.Currency.CalculateExchange(price, usdCurrency) : 0,
                        Supply = supply
                    });
                }
            }

            var missingItems = dbItems.Where(x => !rapidSkinsAppItems.Any(y => x.Name == y.MarketHashName) && x.Item.BuyPrices.ContainsKey(RapidSkins));
            foreach (var missingItem in missingItems)
            {
                missingItem.Item.UpdateBuyPrices(RapidSkins, null);
            }

            await _db.SaveChangesAsync();

            await _statisticsService.PatchDictionaryValueAsync<MarketType, MarketStatusStatistic>(statisticsKey, RapidSkins, x =>
            {
                x.TotalItems = rapidSkinsItemGroups.Count();
                x.TotalListings = rapidSkinsAppItems.Sum(x => x.Stack?.Sum(y => y.Amount) ?? 0);
                x.LastUpdatedItemsOn = DateTimeOffset.Now;
                x.LastUpdatedItemsDuration = stopwatch.Elapsed;
                x.LastUpdateErrorOn = null;
                x.LastUpdateError = null;
            });
        }
        catch (Exception ex)
        {
            try
            {
                logger.LogError(ex, $"Failed to update market item price information from RapidSkins (appId: {app.SteamId}). {ex.Message}");
                await _statisticsService.PatchDictionaryValueAsync<MarketType, MarketStatusStatistic>(statisticsKey, RapidSkins, x =>
                {
                    x.LastUpdateErrorOn = DateTimeOffset.Now;
                    x.LastUpdateError = ex.Message;
                });
            }
            catch (Exception)
            {
                logger.LogError(ex, $"Failed to update market item price statistics for RapidSkins (appId: {app.SteamId}). {ex.Message}");
            }
        }
    }
}
