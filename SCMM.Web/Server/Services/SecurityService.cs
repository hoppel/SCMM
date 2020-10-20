﻿using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SCMM.Steam.Client;
using SCMM.Steam.Shared;
using SCMM.Steam.Shared.Community.Requests.Html;
using SCMM.Web.Server.Data;
using SCMM.Web.Server.Data.Models.Steam;
using SCMM.Web.Server.Extensions;
using SCMM.Web.Shared;
using SCMM.Web.Shared.Domain;
using SteamWebAPI2.Interfaces;
using SteamWebAPI2.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ClaimTypes = SCMM.Web.Server.Data.Models.ClaimTypes;

namespace SCMM.Web.Server.Services
{
    public class SecurityService
    {
        private readonly SteamDbContext _db;
        private readonly SteamConfiguration _cfg;
        private readonly SteamCommunityClient _communityClient;

        public SecurityService(SteamDbContext db, IConfiguration cfg, SteamCommunityClient communityClient)
        {
            _db = db;
            _cfg = cfg?.GetSteamConfiguration();
            _communityClient = communityClient;
        }

        public async Task<ClaimsIdentity> LoginSteamProfileAsync(string steamId)
        {
            if (string.IsNullOrEmpty(steamId))
            {
                throw new ArgumentNullException(nameof(steamId));
            }

            // Obtain the actual SteamID (claim format is: https://steamcommunity.com/openid/id/<steamid>)
            steamId = Regex.Match(steamId, SteamConstants.SteamProfileIdRegex).Groups.OfType<Capture>().LastOrDefault()?.Value;
            if (string.IsNullOrEmpty(steamId))
            {
                throw new ArgumentException(nameof(steamId), $"Unable to parse SteamID from '{steamId}'");
            }

            // Load the profile from our database (if it exists)
            var profileQuery = _db.SteamProfiles
                .Include(x => x.Language)
                .Include(x => x.Currency)
                .Where(x => x.SteamId == steamId || x.ProfileId == steamId)
                .Select(x => new
                {
                    Profile = x,
                    IsCreator = x.WorkshopFiles.Any(x => x.AcceptedOn != null),
                    IsDonator = x.DonatorLevel > 0
                })
                .FirstOrDefault();
            var profile = profileQuery?.Profile;

            // Update any dynamic roles that are missing
            var dynamicRoles = new List<string>();
            if (profileQuery?.IsCreator == true)
            {
                dynamicRoles.Add(Roles.Creator);
            }
            if (profileQuery?.IsDonator == true)
            {
                dynamicRoles.Add(Roles.Donator);
            }
            if (dynamicRoles.Any())
            {
                profile.Roles = new Data.Types.PersistableStringCollection(
                    profile.Roles.Union(dynamicRoles)
                );
            }

            // Update the extended profile information from Steam
            // Is this a 64-bit SteamID?
            if (Int64.TryParse(steamId, out _))
            {
                var steamWebInterfaceFactory = new SteamWebInterfaceFactory(_cfg.ApplicationKey);
                var steamUser = steamWebInterfaceFactory.CreateSteamWebInterface<SteamUser>();
                var response = await steamUser.GetPlayerSummaryAsync(UInt64.Parse(steamId));
                if (response?.Data != null)
                {
                    var profileId = response.Data.ProfileUrl;
                    if (!String.IsNullOrEmpty(profileId))
                    {
                        profileId = (Regex.Match(profileId, SteamConstants.SteamProfileIdRegex).Groups.OfType<Capture>().LastOrDefault()?.Value ?? profileId);
                    }
                    if (String.IsNullOrEmpty(profileId))
                    {
                        profileId = (Regex.Match(profileId, SteamConstants.SteamProfileIdRegex).Groups.OfType<Capture>().LastOrDefault()?.Value ?? profileId);
                    }

                    profile = profile ?? new SteamProfile()
                    {
                        SteamId = steamId,
                        ProfileId = profileId
                    };

                    profile.Name = response.Data.Nickname?.Trim();
                    profile.AvatarUrl = response.Data.AvatarMediumUrl;
                    profile.AvatarLargeUrl = response.Data.AvatarFullUrl;
                    profile.Country = response.Data.CountryCode;
                }
            }

            // Else, it's probably a string SteamID
            else
            {
                var profileId = steamId;
                var response = await _communityClient.GetProfile(new SteamProfilePageRequest()
                {
                    ProfileId = profileId,
                    Xml = true
                });
                if (response != null)
                {
                    profile = profile ?? new SteamProfile()
                    {
                        SteamId = response.SteamID64.ToString(),
                        ProfileId = profileId
                    };

                    profile.Name = response.SteamID?.Trim();
                    profile.AvatarUrl = response.AvatarMedium;
                    profile.AvatarLargeUrl = response.AvatarFull;
                    profile.Country = response.Location;
                }
            }

            // Update the last signin timestamp
            profile.LastSignedInOn = DateTimeOffset.Now;

            // Add the profile to our database (if missing)
            if (profile.Id == Guid.Empty)
            {
                _db.SteamProfiles.Add(profile);
            }

            _db.SaveChanges();

            // Build a identity for the profile
            return new ClaimsIdentity(
                GetClaimsFromSteamProfile(profile),
                null,
                ClaimTypes.Name,
                ClaimTypes.Role
            );
        }

        private IEnumerable<Claim> GetClaimsFromSteamProfile(SteamProfile profile)
        {
            var claims = new List<Claim>();
            claims.AddIfMissing(new Claim(ClaimTypes.Id, profile.Id.ToString()));
            if (!String.IsNullOrEmpty(profile.SteamId))
            {
                claims.AddIfMissing(new Claim(ClaimTypes.SteamId, profile.SteamId));
            }
            if (!String.IsNullOrEmpty(profile.ProfileId))
            {
                claims.AddIfMissing(new Claim(ClaimTypes.ProfileId, profile.ProfileId));
            }
            if (!String.IsNullOrEmpty(profile.Name))
            {
                claims.AddIfMissing(new Claim(ClaimTypes.Name, profile.Name));
            }
            if (!String.IsNullOrEmpty(profile.AvatarUrl))
            {
                claims.AddIfMissing(new Claim(ClaimTypes.AvatarUrl, profile.AvatarUrl));
            }
            if (!String.IsNullOrEmpty(profile.AvatarLargeUrl))
            {
                claims.AddIfMissing(new Claim(ClaimTypes.AvatarLargeUrl, profile.AvatarLargeUrl)); ;
            }
            if (!String.IsNullOrEmpty(profile.Country))
            {
                claims.AddIfMissing(new Claim(ClaimTypes.Country, profile.Country));
            }
            if (profile.Language != null)
            {
                claims.AddIfMissing(new Claim(ClaimTypes.Language, profile.Language.Name));
            }
            if (profile.Currency != null)
            {
                claims.AddIfMissing(new Claim(ClaimTypes.Currency, profile.Currency.Name));
            }
            foreach (var role in profile.Roles)
            {
                claims.AddIfMissing(new Claim(ClaimTypes.Role, role));
            }
            return claims;
        }
    }
}