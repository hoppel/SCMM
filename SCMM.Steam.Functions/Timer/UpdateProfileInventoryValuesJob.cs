﻿using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using SCMM.Steam.Data.Store;

namespace SCMM.Steam.Functions.Timer;

public class UpdateProfileInventoryValuesJob
{
    private readonly SteamDbContext _db;

    public UpdateProfileInventoryValuesJob(SteamDbContext db)
    {
        _db = db;
    }

    [Function("Update-Profile-Inventory-Values")]
    public async Task Run([TimerTrigger("0 45 * * * *")] /* every hour, 45 minutes past the hour */ TimerInfo timerInfo, FunctionContext context)
    {
        using (var transaction = await _db.Database.BeginTransactionAsync())
        {
            await _db.Database.ExecuteSqlInterpolatedAsync(@$"
                UPDATE v 
                SET
                    [Items] = ISNULL((
                        SELECT SUM(i.Quantity)
                        FROM [SteamProfileInventoryItems] i
                        WHERE i.ProfileId = v.ProfileId AND i.AppId = v.AppId
                    ), 0),
	                [MarketValue] = ISNULL((
                        SELECT SUM(i.Quantity * ISNULL(m.SellOrderLowestPrice, ISNULL(m.BuyNowPrice, 0)))
                        FROM [steamprofileinventoryitems] i
                        LEFT OUTER JOIN [SteamMarketItems] m ON m.DescriptionId = i.DescriptionId
                        WHERE i.ProfileId = v.ProfileId AND i.AppId = v.AppId
                    ), 0)
                FROM [SteamProfileInventoryValues] v
            ");

            await transaction.CommitAsync();
        }
    }
}