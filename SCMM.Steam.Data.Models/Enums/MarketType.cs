﻿using SCMM.Steam.Data.Models.Attributes;
using System.ComponentModel.DataAnnotations;

namespace SCMM.Steam.Data.Models.Enums
{
    /// <summary>
    /// All known game item markets
    /// </summary>
    public enum MarketType : byte
    {
        [Display(Name = "Unknown")]
        Unknown = 0,

        [Display(Name = "Steam Store")]
        [Market(Constants.RustAppId, Constants.CSGOAppId, Constants.UnturnedAppId, IsFirstParty = true, Color = "#171A21")]
        [BuyFrom(Url = "https://store.steampowered.com/itemstore/{0}/", AcceptedPayments = PriceFlags.Cash)]
        SteamStore = 1,

        [Display(Name = "Steam Community Market")]
        [Market(Constants.RustAppId, Constants.CSGOAppId, Constants.UnturnedAppId, IsFirstParty = true, Color = "#171A21")]
        [BuyFrom(Url = "https://steamcommunity.com/market/listings/{0}/{3}", AcceptedPayments = PriceFlags.Cash)]
        [SellTo(Url = "https://steamcommunity.com/market/listings/{0}/{3}", AcceptedPayments = PriceFlags.Cash, FeeRate = 13.0f /* 13% */)]
        SteamCommunityMarket = 2,

        [Display(Name = "Skinport")]
        [Market(Constants.RustAppId, Constants.CSGOAppId, Color = "#FA490A", AffiliateUrl = "https://skinport.com/r/scmm")]
        [BuyFrom(Url = "https://skinport.com/{1}/market?r=scmm&item={3}", AcceptedPayments = PriceFlags.Cash)]
        Skinport = 10,

        [Display(Name = "LOOT.Farm")]
        [Market(Constants.RustAppId, Constants.CSGOAppId, Color = "#06FDDE")]
        [BuyFrom(Url = "https://loot.farm/", AcceptedPayments = PriceFlags.Crypto, BonusBalanceMultiplier = 0.76923076923076923076923076923077f /* 30% */)]
        [BuyFrom(Url = "https://loot.farm/", AcceptedPayments = PriceFlags.Cash, BonusBalanceMultiplier = 0.83333333333333333333333333333333f /* 20% */)]
        [BuyFrom(Url = "https://loot.farm/", AcceptedPayments = PriceFlags.Trade)]
        LootFarm = 11,

        [Display(Name = "Swap.gg")]
        [Market(Constants.RustAppId, Constants.CSGOAppId, Color = "#15C9AF", AffiliateUrl = "https://swap.gg/?r=xu9CNezP5w")]
        [BuyFrom(Url = "https://swap.gg/?r=xu9CNezP5w&game={0}", AcceptedPayments = PriceFlags.Cash, BonusBalanceMultiplier = 0.83333333333333333333333333333333f /* 20% */)]
        [BuyFrom(Url = "https://swap.gg/?r=xu9CNezP5w&game={0}", AcceptedPayments = PriceFlags.Trade)]
        SwapGGTrade = 12,

        [Obsolete("Discontinued; merged with main site on https://swap.gg/")]
        [Display(Name = "Swap.gg (Market)")]
        [Market(Constants.RustAppId, Constants.CSGOAppId, Color = "#15C9AF")]
        [BuyFrom(Url = "https://market.swap.gg/{1}?search={3}", AcceptedPayments = PriceFlags.Cash)]
        SwapGGMarket = 13,

        [Display(Name = "Tradeit.gg")]
        [Market(Constants.RustAppId, Constants.CSGOAppId, Color = "#27273F", AffiliateUrl = "https://tradeit.gg/?aff=scmm")]
        [BuyFrom(Url = "https://tradeit.gg/{1}/store?aff=scmm&search={3}", AcceptedPayments = PriceFlags.Cash, BonusBalanceMultiplier = 0.74074074074074074074074074074074f /* 35% */, SurchargeFixedAmount = 30 /* $0.30 */, SurchargePercentage = 0.028f /* 0.28% */ )]
        [BuyFrom(Url = "https://tradeit.gg/{1}/store?aff=scmm&search={3}", AcceptedPayments = PriceFlags.Crypto, BonusBalanceMultiplier = 0.74074074074074074074074074074074f /* 35% */)]
        [BuyFrom(Url = "https://tradeit.gg/{1}/trade?aff=scmm&search={3}", AcceptedPayments = PriceFlags.Trade)]
        TradeitGG = 14,

        [Obsolete("Discontinued; P2P trades are no longer offered")]
        [Display(Name = "CS.Deals (Trade)")]
        [Market(Constants.RustAppId, Constants.CSGOAppId, Color = "#78FFFF")]
        [BuyFrom(Url = "https://cs.deals/trade-skins?appid={0}", AcceptedPayments = PriceFlags.Trade | PriceFlags.Cash | PriceFlags.Crypto)]
        CSDealsTrade = 15,

        // TODO: Items quantities are not currently supported
        [Display(Name = "CS.Deals")]
        [Market(Constants.RustAppId, Constants.CSGOAppId, Color = "#78FFFF", AffiliateUrl = "https://csdeals.com/new/?ref=njq0nty")]
        [BuyFrom(Url = "https://cs.deals/new?ref=njq0nty&game={1}&sort=price&sort_desc=0&name={3}", AcceptedPayments = PriceFlags.Cash | PriceFlags.Crypto)]
        CSDealsMarketplace = 16,

        // TODO: Update web client to support new APIs
        [Obsolete("APIs have changed, web client needs updating")]
        [Display(Name = "Skin Baron")]
        [Market(Constants.CSGOAppId, Color = "#2A2745")]
        [BuyFrom(Url = "https://skinbaron.de/en/{1}?str={3}&sort=CF", AcceptedPayments = PriceFlags.Cash)]
        SkinBaron = 17,

        [Display(Name = "RUST Skins")]
        [Market(Constants.RustAppId, Color = "#EF7070")]
        [BuyFrom(Url = "https://rustskins.com/market?order=ASC&name={3}", AcceptedPayments = PriceFlags.Crypto)]
        [BuyFrom(Url = "https://rustskins.com/market?order=ASC&name={3}", AcceptedPayments = PriceFlags.Cash, SurchargePercentage = 0.099f /* 9.9% */)]
        RustSkins = 18,

        [Display(Name = "Rust.tm")]
        [Market(Constants.RustAppId, Color = "#4E2918")]
        [BuyFrom(Url = "https://rust.tm/?s=price&t=all&search={3}&sd=asc", AcceptedPayments = PriceFlags.Cash)]
        RustTM = 19,

        [Obsolete("Website is dead")]
        [Display(Name = "RUSTVendor")]
        [Market(Constants.RustAppId)]
        [BuyFrom(Url = "https://rustvendor.com/trade", AcceptedPayments = PriceFlags.Trade | PriceFlags.Cash)]
        RustVendor = 20,

        [Obsolete("Needs to be revalidated")]
        [Display(Name = "RustyTrade")]
        [Market(Constants.RustAppId)]
        [BuyFrom(Url = "https://rustytrade.com/", AcceptedPayments = PriceFlags.Trade)]
        RustyTrade = 21,

        [Display(Name = "CS.TRADE")]
        [Market(Constants.RustAppId, Constants.CSGOAppId, Color = "#F3C207", AffiliateUrl = "https://cs.trade/ref/SCMM")]
        [BuyFrom(Url = "https://cs.trade/ref/SCMM#trader", AcceptedPayments = PriceFlags.Cash, BonusBalanceMultiplier = 0.66666666666666666666666666666667f /* 50% */)]
        [BuyFrom(Url = "https://cs.trade/ref/SCMM#trader", AcceptedPayments = PriceFlags.Trade)]
        CSTrade = 22,

        [Display(Name = "iTrade.gg")]
        [Market(Constants.RustAppId, Color = "#EA473B", AffiliateUrl = "https://itrade.gg/?ref=scmm")]
        [BuyFrom(Url = "https://itrade.gg/trade/{1}?search={3}&ref_code=scmm", AcceptedPayments = PriceFlags.Trade)]
        iTradegg = 23,

        [Obsolete("Website is dead")]
        [Display(Name = "Trade Skins Fast")]
        [Market(Constants.RustAppId, Constants.CSGOAppId)]
        [BuyFrom(Url = "https://tradeskinsfast.com/", AcceptedPayments = PriceFlags.Trade)]
        TradeSkinsFast = 24,

        [Display(Name = "SkinsMonkey")]
        [Market(Constants.RustAppId, Constants.CSGOAppId, Color = "#F5C71B")]
        [BuyFrom(Url = "https://skinsmonkey.com/trade", AcceptedPayments = PriceFlags.Cash | PriceFlags.Crypto, BonusBalanceMultiplier = 0.74074074074074074074074074074074f /* 35% */)]
        [BuyFrom(Url = "https://skinsmonkey.com/trade", AcceptedPayments = PriceFlags.Trade)]
        SkinsMonkey = 25,

        [Display(Name = "Skin Swap")]
        [Market(Constants.RustAppId, Constants.CSGOAppId, Color = "#FF4B4B", AffiliateUrl = "https://skinswap.com/r/scmm")]
        [BuyFrom(Url = "https://skinswap.com/r/scmm", AcceptedPayments = PriceFlags.Cash | PriceFlags.Crypto, BonusBalanceMultiplier = 0.71428571428571428571428571428571f /* 40% */)]
        [BuyFrom(Url = "https://skinswap.com/r/scmm", AcceptedPayments = PriceFlags.Trade)]
        SkinSwap = 26,

        // TODO: Restricted to 100 items per query, too slow for CSGO items
        [Display(Name = "DMarket")]
        [Market(Constants.RustAppId, /*Constants.CSGOAppId,*/ Color = "#8dd294", AffiliateUrl = "https://dmarket.com?ref=6tlej6xqvD")]
        [BuyFrom(Url = "https://dmarket.com/ingame-items/item-list/{1}-skins?ref=6tlej6xqvD&title={3}", AcceptedPayments = PriceFlags.Cash, SurchargePercentage = 0.0385f /* 3.85% */, SurchargeFixedAmount = 30 /* $0.30 */)]
        [BuyFrom(Url = "https://dmarket.com/ingame-items/item-list/{1}-skins?ref=6tlej6xqvD&title={3}", AcceptedPayments = PriceFlags.Crypto, SurchargePercentage = 0.02f /* 2% */ )]
        [BuyFrom(Url = "https://dmarket.com/ingame-items/item-list/{1}-skins?ref=6tlej6xqvD&exchangeTab=exchange&title={3}", AcceptedPayments = PriceFlags.Trade)]
        DMarket = 28,

        // TODO: Restricted to 100 items per query, too slow for CSGO items
        [Obsolete("Discontinued; P2P trades are no longer offered")]
        [Display(Name = "DMarket (F2F)")]
        [Market(Constants.RustAppId, /*Constants.CSGOAppId,*/ Color = "#8dd294")]
        [BuyFrom(Url = "https://dmarket.com/ingame-items/item-list/{1}-skins?ref=6tlej6xqvD&exchangeTab=f2fOffers&title={3}", AcceptedPayments = PriceFlags.Cash | PriceFlags.Crypto)]
        DMarketF2F = 29,

        // TODO: Restricted to 80 items per query, too slow for CSGO items
        [Display(Name = "BUFF")]
        [Market(/*Constants.CSGOAppId,*/ Color = "#FFFFFF")]
        [BuyFrom(Url = "https://buff.163.com/market/{1}#tab=selling&sort_by=price.asc&search={3}", AcceptedPayments = PriceFlags.Cash)]
        Buff = 30,

        [Display(Name = "Waxpeer")]
        [Market(Constants.RustAppId, Constants.CSGOAppId, Color = "#FFFFFF", AffiliateUrl = "https://waxpeer.com/r/scmm")]
        [BuyFrom(Url = "https://waxpeer.com/r/scmm?game={1}&sort=ASC&order=price&all=0&exact=0&search={3}", AcceptedPayments = PriceFlags.Trade | PriceFlags.Cash | PriceFlags.Crypto)]
        Waxpeer = 31,

        [Display(Name = "ShadowPay")]
        [Market(Constants.RustAppId, Constants.CSGOAppId, Color = "#30bd91", AffiliateUrl = "https://shadowpay.com?utm_campaign=e6PlQUT3mUC06NL")]
        [BuyFrom(Url = "https://shadowpay.com/en/{1}-items?utm_campaign=e6PlQUT3mUC06NL&price_from=0&price_to=0&currency=USD&search={3}&sort_column=price&sort_dir=asc", AcceptedPayments = PriceFlags.Trade | PriceFlags.Cash | PriceFlags.Crypto)]
        ShadowPay = 32,

        [Display(Name = "Mannco.store")]
        [Market(Constants.RustAppId, Constants.CSGOAppId, Color = "#5ad1e8")]
        [BuyFrom(Url = "https://mannco.store/{1}?&search={3}&page=1", AcceptedPayments = PriceFlags.Trade | PriceFlags.Cash | PriceFlags.Crypto)]
        ManncoStore = 33,

        [Display(Name = "RapidSkins")]
        [Market(Constants.RustAppId, Constants.CSGOAppId, Color = "#ffd500", AffiliateUrl = "https://rapidskins.com/a/scmm")]
        [BuyFrom(Url = "https://rapidskins.com/a/scmm", AcceptedPayments = PriceFlags.Cash, SurchargePercentage = 0.02f /* 5% */)]
        [BuyFrom(Url = "https://rapidskins.com/a/scmm", AcceptedPayments = PriceFlags.Crypto, SurchargePercentage = 0.02f /* 2% */)]
        [BuyFrom(Url = "https://rapidskins.com/a/scmm", AcceptedPayments = PriceFlags.Trade)]
        RapidSkins = 34,

        [Display(Name = "Skin Serpent")]
        [Market(Constants.RustAppId, Color = "#29d14a")]
        [BuyFrom(Url = "https://skinserpent.com/?sortBy=P_DESC&search={3}", AcceptedPayments = PriceFlags.Cash | PriceFlags.Crypto, SurchargePercentage = 0.02f /* 5% */)]
        [BuyFrom(Url = "https://skinserpent.com/?sortBy=P_DESC&search={3}", AcceptedPayments = PriceFlags.Trade)]
        SkinSerpent = 35,

        [Display(Name = "Rustyloot.gg")]
        [Market(Constants.RustAppId, Color = "#ffb135", IsCasino = true /*, AffiliateUrl = "https://rustyloot.gg/r/SCMM" */)]
        [BuyFrom(Url = "https://rustyloot.gg/?withdraw=true&rust=true", AcceptedPayments = PriceFlags.Cash, BonusBalanceMultiplier = 0.64516129032258064516129032258065f /* 55% */,
                 HouseCurrencyName = "Coin", HouseCurrencyScale = 2, HouseCurrencyToUsdExchangeRate = 0.64516129032258064516129032258065)]
        [BuyFrom(Url = "https://rustyloot.gg/?withdraw=true&rust=true", AcceptedPayments = PriceFlags.Crypto, BonusBalanceMultiplier = 0.63492063492063492063492063492063f /* 57.5% */,
                 HouseCurrencyName = "Coin", HouseCurrencyScale = 2, HouseCurrencyToUsdExchangeRate = 0.64516129032258064516129032258065)]
        [BuyFrom(Url = "https://rustyloot.gg/?withdraw=true&rust=true", AcceptedPayments = PriceFlags.Trade,
                 HouseCurrencyName = "Coin", HouseCurrencyScale = 2, HouseCurrencyToUsdExchangeRate = 0.64516129032258064516129032258065)]
        Rustyloot = 36,

        [Display(Name = "SnipeSkins")]
        [Market(Constants.RustAppId, Color = "#f8546f")]
        [BuyFrom(Url = "https://snipeskins.com/buy-rust-skins?search={3}&sortBy=price&sortDir=asc", AcceptedPayments = PriceFlags.Cash | PriceFlags.Crypto)]
        SnipeSkins = 37,

        /*
         
         MARKETS TO BE INVESTIGATED:
         BUY:    https://skin.land/market/rust/
         BUY:    https://buff.market/ (western sister-site for https://buff.163.com/, no rust)
         BUY:    https://gameflip.com/shop/in-game-items/rust?status=onsale&limit=100&platform=252490&sort=price%3Aasc (has low stock)
         BUY:    https://lis-skins.ru/market/rust/ (has overly aggressive CloudFlare WAF policies, need to scrap HTML code)
         BUY:    https://gamerall.com/rust (has overly aggressive CloudFlare WAF policies)
         BUY:    https://traderust.com/
         BUY:    https://skinsdrip.com/ 
         SELL:   https://skincashier.com/
         SELL:   https://skins.cash/
         GAMBLE: https://bandit.camp/ (gambling site)
         GAMBLE: https://rustbet.com (no inventory stock)
         GAMBLE: https://rustchance.com/ (low inventory stock)
         GAMBLE: https://rustreaper.com (bad reviews?)
         GAMBLE: https://rustysaloon.com (dead?)
         
         MARKET AGGRAGATORS:
         https://pricempire.com/api

        */

    }
}
