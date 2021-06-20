﻿using AutoMapper;
using CommandQuery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SCMM.Shared.Data.Models;
using SCMM.Shared.Data.Models.Extensions;
using SCMM.Shared.Data.Store.Extensions;
using SCMM.Shared.Web.Extensions;
using SCMM.Steam.API.Commands;
using SCMM.Steam.API.Queries;
using SCMM.Steam.Data.Models.Enums;
using SCMM.Steam.Data.Store;
using SCMM.Web.Data.Models;
using SCMM.Web.Data.Models.Extensions;
using SCMM.Web.Data.Models.UI.Profile;
using SCMM.Web.Data.Models.UI.Profile.Inventory;
using SCMM.Web.Server.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SCMM.Web.Server.API.Controllers
{
    [ApiController]
    [Route("api/profile")]
    public class ProfileController : ControllerBase
    {
        private readonly ILogger<ProfileController> _logger;
        private readonly IConfiguration _configuration;
        private readonly SteamDbContext _db;
        private readonly ICommandProcessor _commandProcessor;
        private readonly IQueryProcessor _queryProcessor;
        private readonly IMapper _mapper;

        public ProfileController(ILogger<ProfileController> logger, IConfiguration configuration, SteamDbContext db, ICommandProcessor commandProcessor, IQueryProcessor queryProcessor, IMapper mapper)
        {
            _logger = logger;
            _configuration = configuration;
            _db = db;
            _commandProcessor = commandProcessor;
            _queryProcessor = queryProcessor;
            _mapper = mapper;
        }

        /// <summary>
        /// Get your profile information
        /// </summary>
        /// <remarks>
        /// The language used for text localisation can be changed by defining the <code>Language</code> header and setting it to a supported language identifier (e.g. 'english').
        /// The currency used to represent monetary values can be changed by defining <code>Currency</code> in the request headers or query string and setting it to a supported three letter ISO 4217 currency code (e.g. 'USD').
        /// </remarks>
        /// <response code="200">If the session is authentication, your Steam profile information is returned. If the session is unauthenticated, a generic guest profile is returned.</response>
        /// <response code="500">If the server encountered a technical issue completing the request.</response>
        [AllowAnonymous]
        [HttpGet]
        [ProducesResponseType(typeof(MyProfileDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetMyProfile()
        {
            var defaultProfile = new MyProfileDTO()
            {
                Name = "Guest",
                Language = this.Language(),
                Currency = this.Currency()
            };

            // If the user is authenticated, use their database profile
            if (User.Identity.IsAuthenticated)
            {
                var profileId = User.Id();
                var profile = await _db.SteamProfiles
                    .AsNoTracking()
                    .Include(x => x.Language)
                    .Include(x => x.Currency)
                    .FirstOrDefaultAsync(x => x.Id == profileId);

                return Ok(
                    _mapper.Map<SteamProfile, MyProfileDTO>(profile, this) ?? defaultProfile
                );
            }

            // Else, use a transient guest profile
            else
            {
                return Ok(defaultProfile);
            }
        }

        /// <summary>
        /// Update your profile information
        /// </summary>
        /// <remarks>This API requires authentication</remarks>
        /// <param name="command">
        /// Information to be updated to your profile. 
        /// Any fields that are <code>null</code> are ignored (not updated).
        /// </param>
        /// <response code="200">If the profile was updated successfully.</response>
        /// <response code="400">If the request data is malformed/invalid.</response>
        /// <response code="401">If the request is unauthenticated (login first).</response>
        /// <response code="500">If the server encountered a technical issue completing the request.</response>
        [Authorize(AuthorizationPolicies.User)]
        [HttpPut]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> SetMyProfile([FromBody] UpdateProfileCommand command)
        {
            if (command == null)
            {
                return BadRequest($"No data to update");
            }

            var profileId = User.Id();
            var profile = await _db.SteamProfiles
                .Include(x => x.Language)
                .Include(x => x.Currency)
                .FirstOrDefaultAsync(x => x.Id == profileId);

            if (profile == null)
            {
                return BadRequest($"Profile not found");
            }

            if (command.DiscordId != null)
            {
                profile.DiscordId = command.DiscordId;
            }
            if (command.Language != null)
            {
                profile.Language = _db.SteamLanguages.FirstOrDefault(x => x.Name == command.Language);
            }
            if (command.Currency != null)
            {
                profile.Currency = _db.SteamCurrencies.FirstOrDefault(x => x.Name == command.Currency);
            }
            if (command.TradeUrl != null)
            {
                profile.TradeUrl = command.TradeUrl;
            }
            if (command.GamblingOffset != null)
            {
                profile.GamblingOffset = command.GamblingOffset.Value;
            }

            await _db.SaveChangesAsync();
            return Ok();
        }

        /// <summary>
        /// Get profile information
        /// </summary>
        /// <param name="id">Valid Steam ID64, Custom URL, or Profile URL</param>
        /// <response code="200">Steam profile information.</response>
        /// <response code="400">If the request data is malformed/invalid.</response>
        /// <response code="404">If the profile cannot be found or the profile is private.</response>
        /// <response code="500">If the server encountered a technical issue completing the request.</response>
        [AllowAnonymous]
        [HttpGet("{id}/summary")]
        [ProducesResponseType(typeof(ProfileDetailedDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetProfileSummary([FromRoute] string id)
        {
            if (String.IsNullOrEmpty(id))
            {
                return BadRequest("ID is invalid");
            }

            // Load the profile
            var importedProfile = await _commandProcessor.ProcessWithResultAsync(new ImportSteamProfileRequest()
            {
                ProfileId = id
            });

            var profile = importedProfile?.Profile;
            if (profile == null)
            {
                return NotFound($"Profile not found");
            }

            await _db.SaveChangesAsync();
            return Ok(
                _mapper.Map<SteamProfile, ProfileDetailedDTO>(
                    profile, this
                )
            );
        }

        /// <summary>
        /// Synchronise Steam profile inventory
        /// </summary>
        /// <param name="id">Valid Steam ID64, Custom URL, or Profile URL</param>
        /// <param name="force">If true, the inventory will always be fetched from Steam. If false, sync calls to Steam are cached for one hour</param>
        /// <response code="200">If the inventory was successfully synchronised.</response>
        /// <response code="400">If the request data is malformed/invalid.</response>
        /// <response code="404">If the profile cannot be found or is the inventory is private.</response>
        /// <response code="500">If the server encountered a technical issue completing the request.</response>
        [AllowAnonymous]
        [HttpPost("{id}/inventory/sync")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> PostInventorySync([FromRoute] string id, [FromQuery] bool force = false)
        {
            if (String.IsNullOrEmpty(id))
            {
                return BadRequest("ID is invalid");
            }

            // Reload the profile's inventory
            var importedInventory = await _commandProcessor.ProcessWithResultAsync(new ImportSteamProfileInventoryRequest()
            {
                ProfileId = id,
                Force = force
            });

            var profile = importedInventory?.Profile;
            if (profile == null)
            {
                return NotFound($"Profile not found");
            }
            if (profile?.Privacy != SteamVisibilityType.Public)
            {
                return NotFound($"Profile inventory is private");
            }

            await _db.SaveChangesAsync();
            return Ok();
        }

        /// <summary>
        /// Get Steam profile inventory and calculate the market value
        /// </summary>
        /// <remarks>
        /// The currency used to represent monetary values can be changed by defining <code>Currency</code> in the request headers or query string and setting it to a supported three letter ISO 4217 currency code (e.g. 'USD').
        /// Inventory mosaic images automatically expire after 7 days; After which, the URL will return a 404 response.
        /// </remarks>
        /// <param name="id">Valid Steam ID64, Custom URL, or Profile URL</param>
        /// <param name="generateInventoryMosaic">If true, a mosaic image of the highest valued items will be generated in the response. If false, the mosaic will be <code>null</code>.</param>
        /// <param name="mosaicTileSize">The size (in pixel) to render each item within the mosaic image (if enabled)</param>
        /// <param name="mosaicColumns">The number of item columns to render within the mosaic image (if enabled)</param>
        /// <param name="mosaicRows">The number of item rows to render within the mosaic image (if enabled)</param>
        /// <param name="force">If true, the inventory will always be fetched from Steam. If false, calls to Steam are cached for one hour</param>
        /// <response code="200">Profile inventory value.</response>
        /// <response code="400">If the request data is malformed/invalid.</response>
        /// <response code="404">If the profile cannot be found or if the inventory is private/empty.</response>
        /// <response code="500">If the server encountered a technical issue completing the request.</response>
        [AllowAnonymous]
        [HttpGet("{id}/inventory/value")]
        [ProducesResponseType(typeof(ProfileInventoryValueDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetInventoryValue([FromRoute] string id, [FromQuery] bool generateInventoryMosaic = false, [FromQuery] int mosaicTileSize = 128, [FromQuery] int mosaicColumns = 5, [FromQuery] int mosaicRows = 5, [FromQuery] bool force = false)
        {
            if (String.IsNullOrEmpty(id))
            {
                return BadRequest("ID is invalid");
            }

            // Load the profile
            var importedProfile = await _commandProcessor.ProcessWithResultAsync(new ImportSteamProfileRequest()
            {
                ProfileId = id
            });

            var profile = importedProfile?.Profile;
            if (profile == null)
            {
                return NotFound("Profile not found");
            }

            // Reload the profiles inventory
            var importedInventory = await _commandProcessor.ProcessWithResultAsync(new ImportSteamProfileInventoryRequest()
            {
                ProfileId = profile.Id.ToString(),
                Force = force
            });
            if (importedInventory?.Profile?.Privacy != SteamVisibilityType.Public)
            {
                return NotFound("Profile inventory is private");
            }

            await _db.SaveChangesAsync();

            // Calculate the profiles inventory totals
            var inventoryTotals = await _queryProcessor.ProcessAsync(new GetSteamProfileInventoryTotalsRequest()
            {
                ProfileId = profile.SteamId,
                CurrencyId = this.Currency().Id.ToString()
            });
            if (inventoryTotals == null)
            {
                return NotFound("Profile inventory is empty (no marketable items)");
            }

            // Generate the profiles inventory thumbnail
            var inventoryThumbnail = (GenerateSteamProfileInventoryThumbnailResponse)null;
            if (generateInventoryMosaic)
            {
                inventoryThumbnail = await _commandProcessor.ProcessWithResultAsync(new GenerateSteamProfileInventoryThumbnailRequest()
                {
                    ProfileId = profile.SteamId,
                    TileSize = mosaicTileSize,
                    Columns = mosaicColumns,
                    Rows = mosaicRows,
                    ExpiresOn = DateTimeOffset.Now.AddDays(7)
                });
            }

            await _db.SaveChangesAsync();

            return Ok(
                new ProfileInventoryValueDTO()
                {
                    SteamId = profile.SteamId,
                    Name = profile.Name,
                    AvatarUrl = profile.AvatarUrl,
                    InventoryMosaicUrl = inventoryThumbnail != null ? $"{_configuration.GetWebsiteUrl()}/api/image/{inventoryThumbnail.Image?.Id}" : null,
                    Items = inventoryTotals.TotalItems,
                    Invested = inventoryTotals.TotalInvested,
                    MarketValue = inventoryTotals.TotalMarketValue,
                    Market24hrMovement = inventoryTotals.TotalMarket24hrMovement,
                    ResellValue = inventoryTotals.TotalResellValue,
                    ResellTax = inventoryTotals.TotalResellTax,
                    ResellProfit = inventoryTotals.TotalResellProfit
                }
            );
        }

        /// <summary>
        /// Get profile inventory item totals
        /// </summary>
        /// <remarks>
        /// The currency used to represent monetary values can be changed by defining <code>Currency</code> in the request headers or query string and setting it to a supported three letter ISO 4217 currency code (e.g. 'USD').
        /// </remarks>
        /// <param name="id">Valid Steam ID64, Custom URL, or Profile URL</param>
        /// <response code="200">Profile inventory item totals.</response>
        /// <response code="400">If the request data is malformed/invalid.</response>
        /// <response code="404">If the profile cannot be found or if the inventory is private/empty.</response>
        /// <response code="500">If the server encountered a technical issue completing the request.</response>
        [AllowAnonymous]
        [HttpGet("{id}/inventory/total")]
        [ProducesResponseType(typeof(ProfileInventoryTotalsDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetInventoryTotal([FromRoute] string id)
        {
            if (String.IsNullOrEmpty(id))
            {
                return BadRequest("ID is invalid");
            }

            var inventoryTotals = await _queryProcessor.ProcessAsync(new GetSteamProfileInventoryTotalsRequest()
            {
                ProfileId = id,
                CurrencyId = this.Currency().Id.ToString()
            });
            if (inventoryTotals == null)
            {
                return NotFound("Profile inventory is empty (no marketable items)");
            }

            return Ok(
                _mapper.Map<GetSteamProfileInventoryTotalsResponse, ProfileInventoryTotalsDTO>(inventoryTotals, this)
            );
        }

        /// <summary>
        /// Get profile inventory item information
        /// </summary>
        /// <remarks>
        /// The currency used to represent monetary values can be changed by defining <code>Currency</code> in the request headers or query string and setting it to a supported three letter ISO 4217 currency code (e.g. 'USD').
        /// </remarks>
        /// <param name="id">Valid Steam ID64, Custom URL, or Profile URL</param>
        /// <response code="200">Profile inventory item information.</response>
        /// <response code="400">If the request data is malformed/invalid.</response>
        /// <response code="500">If the server encountered a technical issue completing the request.</response>
        [AllowAnonymous]
        [HttpGet("{id}/inventory/items")]
        [ProducesResponseType(typeof(IList<ProfileInventoryItemDescriptionDTO>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetInventoryItems([FromRoute] string id)
        {
            if (String.IsNullOrEmpty(id))
            {
                return BadRequest("ID is invalid");
            }

            var resolvedId = await _queryProcessor.ProcessAsync(new ResolveSteamIdRequest()
            {
                Id = id
            });
            if (resolvedId?.Exists != true)
            {
                return NotFound("Profile not found");
            }

            var profileInventoryItems = await _db.SteamProfileInventoryItems
                .AsNoTracking()
                .Where(x => x.ProfileId == resolvedId.ProfileId)
                .Where(x => x.Description != null)
                .Include(x => x.Description)
                .Include(x => x.Description.App)
                .Include(x => x.Description.StoreItem)
                .Include(x => x.Description.StoreItem.Prices)
                .Include(x => x.Description.MarketItem)
                .Include(x => x.Description.MarketItem.Currency)
                .ToListAsync();

            var profileInventoryItemsSummaries = new List<ProfileInventoryItemDescriptionDTO>();
            foreach (var item in profileInventoryItems)
            {
                if (!profileInventoryItemsSummaries.Any(x => x.Id == item.Description.ClassId))
                {
                    var itemSummary = _mapper.Map<SteamAssetDescription, ProfileInventoryItemDescriptionDTO>(
                        item.Description, this
                    );

                    // Calculate the item's quantity
                    itemSummary.Quantity = profileInventoryItems
                        .Where(x => x.Description.ClassId == item.Description.ClassId)
                        .Sum(x => x.Quantity);

                    profileInventoryItemsSummaries.Add(itemSummary);
                }
            }

            return Ok(
                profileInventoryItemsSummaries.OrderByDescending(x => x.BuyNowPrice)
            );
        }

        /// <summary>
        /// Get profile inventory investment information
        /// </summary>
        /// <remarks>
        /// This API requires authentication.
        /// The currency used to represent monetary values can be changed by defining <code>Currency</code> in the request headers or query string and setting it to a supported three letter ISO 4217 currency code (e.g. 'USD').
        /// </remarks>
        /// <param name="id">Valid Steam ID64, Custom URL, or Profile URL</param>
        /// <param name="filter">Optional search filter. Matches against item name or description</param>
        /// <param name="start">Return items starting at this specific index (pagination)</param>
        /// <param name="count">Number items to be returned (can be less if not enough data)</param>
        /// <param name="sortBy">Sort item property name from <see cref="InventoryInvestmentItemDTO"/></param>
        /// <param name="sortDirection">Sort item direction</param>
        /// <response code="200">Profile inventory investment information.</response>
        /// <response code="400">If the request data is malformed/invalid.</response>
        /// <response code="401">If the request is unauthenticated (login first) or the requested inventory does not belong to the authenticated user.</response>
        /// <response code="500">If the server encountered a technical issue completing the request.</response>
        [Authorize(AuthorizationPolicies.User)]
        [HttpGet("{id}/inventory/investment")]
        [ProducesResponseType(typeof(PaginatedResult<InventoryInvestmentItemDTO>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetInventoryInvestment([FromRoute] string id, [FromQuery] string filter = null, [FromQuery] int start = 0, [FromQuery] int count = 10, [FromQuery] string sortBy = null, [FromQuery] SortDirection sortDirection = SortDirection.Ascending)
        {
            if (String.IsNullOrEmpty(id))
            {
                return BadRequest("ID is invalid");
            }

            var resolvedId = await _queryProcessor.ProcessAsync(new ResolveSteamIdRequest()
            {
                Id = id
            });
            if (resolvedId?.Exists != true || resolvedId.ProfileId == null)
            {
                return NotFound("Profile not found");
            }

            if (!User.Is(resolvedId.ProfileId.Value) && !User.IsInRole(Roles.Administrator))
            {
                _logger.LogError($"Inventory does not belong to you and you do not have permission to view it");
                return Unauthorized($"Inventory does not belong to you and you do not have permission to view it");
            }

            filter = Uri.UnescapeDataString(filter?.Trim() ?? String.Empty);
            var query = _db.SteamProfileInventoryItems
                .AsNoTracking()
                .Where(x => x.ProfileId == resolvedId.ProfileId)
                .Where(x => String.IsNullOrEmpty(filter) || x.Description.Name.ToLower().Contains(filter.ToLower()))
                .Include(x => x.App)
                .Include(x => x.Currency)
                .Include(x => x.Description.MarketItem)
                .Include(x => x.Description.MarketItem.Currency)
                .Include(x => x.Description.StoreItem)
                .Include(x => x.Description.StoreItem.Currency)
                .OrderBy(sortBy, sortDirection);

            var results = await query.PaginateAsync(start, count,
                x => _mapper.Map<SteamProfileInventoryItem, InventoryInvestmentItemDTO>(x, this)
            );

            return Ok(results);
        }

        /// <summary>
        /// Update profile inventory item information
        /// </summary>
        /// <remarks>This API requires authentication</remarks>
        /// <param name="id">Valid Steam ID64, Custom URL, or Profile URL</param>
        /// <param name="itemId">
        /// Inventory item identifier to be updated. 
        /// The item must belong to your (currently authenticated) profile
        /// </param>
        /// <param name="command">
        /// Information to be updated for the item. 
        /// Any fields that are <code>null</code> are ignored (not updated).
        /// </param>
        /// <response code="200">If the inventory item was updated successfully.</response>
        /// <response code="400">If the request data is malformed/invalid.</response>
        /// <response code="401">If the request is unauthenticated (login first) or the requested inventory item does not belong to the authenticated user.</response>
        /// <response code="404">If the inventory item cannot be found.</response>
        /// <response code="500">If the server encountered a technical issue completing the request.</response>
        [Authorize(AuthorizationPolicies.User)]
        [HttpPut("{id}/inventory/item/{itemId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> SetInventoryItem([FromRoute] string id, [FromRoute] Guid itemId, [FromBody] UpdateInventoryItemCommand command)
        {
            if (String.IsNullOrEmpty(id))
            {
                return BadRequest("ID is invalid");
            }
            if (itemId == Guid.Empty)
            {
                return BadRequest("Inventory item GUID is invalid");
            }
            if (command == null)
            {
                return BadRequest($"No data to update");
            }

            var inventoryItem = await _db.SteamProfileInventoryItems.FirstOrDefaultAsync(x => x.Id == itemId);
            if (inventoryItem == null)
            {
                _logger.LogError($"Inventory item was not found");
                return NotFound($"Inventory item  was not found");
            }
            if (!User.Is(inventoryItem.ProfileId) && !User.IsInRole(Roles.Administrator))
            {
                _logger.LogError($"Inventory item does not belong to you and you do not have permission to modify it");
                return Unauthorized($"Inventory item does not belong to you and you do not have permission to modify it");
            }

            if (command.AcquiredBy != null)
            {
                inventoryItem.AcquiredBy = command.AcquiredBy.Value;
                switch (inventoryItem.AcquiredBy)
                {
                    // Items sourced from gambling, gifts, and drops don't need prices
                    case SteamProfileInventoryItemAcquisitionType.Gambling:
                    case SteamProfileInventoryItemAcquisitionType.Gift:
                    case SteamProfileInventoryItemAcquisitionType.Drop:
                        {
                            inventoryItem.CurrencyId = null;
                            inventoryItem.BuyPrice = null;
                            break;
                        }
                }
            }
            if (command.CurrencyGuid != null)
            {
                inventoryItem.CurrencyId = command.CurrencyGuid;
            }
            if (command.BuyPrice != null)
            {
                inventoryItem.BuyPrice = (command.BuyPrice > 0 ? command.BuyPrice : null);
            }

            await _db.SaveChangesAsync();
            return Ok();
        }
    }
}
