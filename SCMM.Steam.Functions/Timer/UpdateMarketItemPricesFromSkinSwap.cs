﻿using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SCMM.Market.SkinSwap.Client;
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

public class UpdateMarketItemPricesFromSkinSwap
{
    private const MarketType SkinSwap = MarketType.SkinSwap;

    private readonly SteamDbContext _db;
    private readonly SkinSwapWebClient _skinSwapWebClient;
    private readonly IStatisticsService _statisticsService;

    public UpdateMarketItemPricesFromSkinSwap(SteamDbContext db, SkinSwapWebClient skinSwapWebClient, IStatisticsService statisticsService)
    {
        _db = db;
        _skinSwapWebClient = skinSwapWebClient;
        _statisticsService = statisticsService;
    }

    [Function("Update-Market-Item-Prices-From-SkinSwap")]
    public async Task Run([TimerTrigger("0 16/30 * * * *")] /* every 30mins */ TimerInfo timerInfo, FunctionContext context)
    {
        if (!SkinSwap.IsEnabled())
        {
            return;
        }

        var logger = context.GetLogger("Update-Market-Item-Prices-From-SkinSwap");

        var appIds = SkinSwap.GetSupportedAppIds().Select(x => x.ToString()).ToArray();
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
            logger.LogTrace($"Updating item price information from SkinSwap (appId: {app.SteamId})");
            await UpdateSkinSwapMarketPricesForApp(logger, app, usdCurrency);
        }
    }

    private async Task UpdateSkinSwapMarketPricesForApp(ILogger logger, SteamApp app, SteamCurrency usdCurrency)
    {
        var statisticsKey = String.Format(StatisticKeys.MarketStatusByAppId, app.SteamId);
        var stopwatch = new Stopwatch();
        try
        {
            stopwatch.Start();

            var skinSwapAppItems = new List<SkinSwapItem>();
            var skinSwapResponse = (SkinSwapResponse<SkinSwapItem[]>)null;
            var offset = 0;
            do
            {
                skinSwapResponse = await _skinSwapWebClient.GetSiteInventoryAsync(app.SteamId, offset);
                if (skinSwapResponse.Data?.Any() == true)
                {
                    skinSwapAppItems.AddRange(skinSwapResponse.Data);
                }
                offset += skinSwapResponse.Data?.Length ?? 0;
            } while (skinSwapResponse?.EndOfResults != true);

            var dbItems = await _db.SteamMarketItems
                .Where(x => x.AppId == app.Id)
                .Select(x => new
                {
                    Name = x.Description.NameHash,
                    Currency = x.Currency,
                    Item = x,
                })
                .ToListAsync();

            foreach (var skinSwapAppItem in skinSwapAppItems)
            {
                var item = dbItems.FirstOrDefault(x => x.Name == skinSwapAppItem.MarketHashName)?.Item;
                if (item != null)
                {
                    var supply = skinSwapAppItem.Overstock?.Count;
                    var price = skinSwapAppItem.Price?.Trade ?? 0;
                    item.UpdateBuyPrices(SkinSwap, new PriceWithSupply
                    {
                        Price = supply > 0 && price > 0 ? item.Currency.CalculateExchange(price, usdCurrency.ExchangeRateMultiplier) : 0,
                        Supply = supply
                    });
                }
            }

            var missingItems = dbItems.Where(x => !skinSwapAppItems.Any(y => x.Name == y.MarketHashName) && x.Item.BuyPrices.ContainsKey(SkinSwap));
            foreach (var missingItem in missingItems)
            {
                missingItem.Item.UpdateBuyPrices(SkinSwap, null);
            }

            await _db.SaveChangesAsync();

            await _statisticsService.PatchDictionaryValueAsync<MarketType, MarketStatusStatistic>(statisticsKey, SkinSwap, x =>
            {
                x.TotalItems = skinSwapAppItems.Count();
                x.TotalListings = skinSwapAppItems.Sum(x => x.Overstock?.Count ?? 0);
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
                logger.LogError(ex, $"Failed to update market item price information from SkinSwap (appId: {app.SteamId}). {ex.Message}");
                await _statisticsService.PatchDictionaryValueAsync<MarketType, MarketStatusStatistic>(statisticsKey, SkinSwap, x =>
                {
                    x.LastUpdateErrorOn = DateTimeOffset.Now;
                    x.LastUpdateError = ex.Message;
                });
            }
            catch (Exception)
            {
                logger.LogError(ex, $"Failed to update market item price statistics for SkinSwap (appId: {app.SteamId}). {ex.Message}");
            }
        }
        finally
        {
            stopwatch.Stop();
        }
    }
}
