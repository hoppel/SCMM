﻿using Blazored.LocalStorage;
using Microsoft.Extensions.Logging;
using SCMM.Web.Shared.Domain.DTOs.Currencies;
using SCMM.Web.Shared.Domain.DTOs.Languages;
using SCMM.Web.Shared.Domain.DTOs.Profiles;
using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace SCMM.Web.Client
{
    public class AppState
    {
        public const string HttpHeaderLanguage = "language";
        public const string HttpHeaderCurrency = "currency";
        public const string HttpHeaderProfile = "profile";

        private readonly ILogger<AppState> Logger;

        public AppState(ILogger<AppState> logger)
        {
            this.Logger = logger;
        }

        public event EventHandler Changed;

        public string LanguageId { get; set; }

        public LanguageDetailedDTO Language => Profile?.Language;

        public string CurrencyId { get; set; }

        public CurrencyDetailedDTO Currency => Profile?.Currency;

        public bool IsValid => (
            !String.IsNullOrEmpty(LanguageId) && !String.IsNullOrEmpty(CurrencyId)
        );

        public string ProfileId { get; set; }

        public ProfileDetailedDTO Profile { get; set; }

        public bool HasProfile => (
            !String.IsNullOrEmpty(ProfileId) && Profile != null && Profile.Id != Guid.Empty
        );

        public void SetHeadersFor(HttpClient client)
        {
            if (!String.IsNullOrEmpty(LanguageId))
            {
                client.DefaultRequestHeaders.Remove(HttpHeaderLanguage);
                client.DefaultRequestHeaders.Add(HttpHeaderLanguage, LanguageId);
            }
            if (!String.IsNullOrEmpty(CurrencyId))
            {
                client.DefaultRequestHeaders.Remove(HttpHeaderCurrency);
                client.DefaultRequestHeaders.Add(HttpHeaderCurrency, CurrencyId);
            }
            if (!String.IsNullOrEmpty(ProfileId))
            {
                client.DefaultRequestHeaders.Remove(HttpHeaderProfile);
                client.DefaultRequestHeaders.Add(HttpHeaderProfile, ProfileId);
            }
        }

        public async Task LoadAsync(ILocalStorageService storage)
        {
            try
            {
                ProfileId = await storage.GetItemAsync<string>(nameof(ProfileId));
                LanguageId = await storage.GetItemAsync<string>(nameof(LanguageId));
                CurrencyId = await storage.GetItemAsync<string>(nameof(CurrencyId));
                Changed?.Invoke(this, new EventArgs());
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to load state from storage");
            }
        }

        public async Task SaveAsync(ILocalStorageService storage)
        {
            try
            {
                if (!String.IsNullOrEmpty(ProfileId))
                {
                    await storage.SetItemAsync<string>(nameof(ProfileId), ProfileId);
                }
                else
                {
                    await storage.RemoveItemAsync(nameof(ProfileId));
                }
                if (!String.IsNullOrEmpty(LanguageId))
                {
                    await storage.SetItemAsync<string>(nameof(LanguageId), LanguageId);
                }
                else
                {
                    await storage.RemoveItemAsync(nameof(LanguageId));
                }
                if (!String.IsNullOrEmpty(CurrencyId))
                {
                    await storage.SetItemAsync<string>(nameof(CurrencyId), CurrencyId);
                }
                else
                {
                    await storage.RemoveItemAsync(nameof(CurrencyId));
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to save state");
            }
        }

        public async Task RefreshAsync(HttpClient http)
        {
            try
            {
                if (IsValid)
                {
                    SetHeadersFor(http);
                    Profile = await http.GetFromJsonAsync<ProfileDetailedDTO>(
                        $"api/profile/me"
                    );
                }
                else
                {
                    Profile = null;
                }
                Changed?.Invoke(this, new EventArgs());
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to refresh state");
            }
        }

        public async Task LoginAsync(HttpClient http, ILocalStorageService storage, string profileId)
        {
            try
            {
                ProfileId = profileId;
                await SaveAsync(storage);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to set state");
            }

            await RefreshAsync(http);
        }

        public async Task LoginAndUpdateProfileAsync(HttpClient http, ILocalStorageService storage, ProfileDTO profile, string country, string language, string currency)
        {
            try
            {
                LanguageId = language;
                CurrencyId = currency;
                ProfileId = profile?.SteamId;
                await SaveAsync(storage);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to set state");
            }

            if (profile != null)
            {
                try
                {
                    SetHeadersFor(http);
                    await http.PutAsJsonAsync("api/profile/me", new UpdateProfileStateCommand()
                    {
                        Country = country,
                        Language = language,
                        Currency = currency
                    });
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, $"Failed to update profile info for '{profile?.SteamId}'");
                }
            }

            await RefreshAsync(http);
        }

        public async Task LogoutAsync(HttpClient http, ILocalStorageService storage)
        {
            try
            {
                LanguageId = null;
                CurrencyId = null;
                ProfileId = null;
                await SaveAsync(storage);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to set state");
            }

            await RefreshAsync(http);
        }
    }
}
