using Azure.Identity;
using CommandQuery.DependencyInjection;
using Coravel;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using SCMM.Azure.AI;
using SCMM.Azure.AI.Extensions;
using SCMM.Azure.ApplicationInsights.Filters;
using SCMM.Azure.ServiceBus.Extensions;
using SCMM.Azure.ServiceBus.Middleware;
using SCMM.Discord.API.Commands;
using SCMM.Redis.Client.Statistics;
using SCMM.Shared.Abstractions.Analytics;
using SCMM.Shared.Abstractions.Messaging;
using SCMM.Shared.Abstractions.Statistics;
using SCMM.Shared.Abstractions.WebProxies;
using SCMM.Shared.API.Extensions;
using SCMM.Shared.API.Messages;
using SCMM.Shared.Data.Models.Json;
using SCMM.Shared.Data.Models.Extensions;
using SCMM.Shared.Web.Client;
using SCMM.Shared.Web.Server.Middleware;
using SCMM.Steam.Abstractions;
using SCMM.Steam.API.Commands;
using SCMM.Steam.Client;
using SCMM.Steam.Client.Extensions;
using SCMM.Steam.Data.Store;
using SCMM.Steam.Job.Server.Jobs;
using SCMM.SteamCMD;
using StackExchange.Redis;
using System.Net;
using System.Reflection;
using CommandQuery;
using SCMM.Steam.Job.Server.Attributes;
using Coravel.Scheduling.Schedule.Interfaces;

JsonSerializerOptionsExtensions.SetGlobalDefaultOptions();

await WebApplication.CreateBuilder(args)
    .ConfigureLogging()
    .ConfigureAppConfiguration()
    .ConfigureServices()
    .Build()
    .Configure()
    .Warmup()
    .RunAsync();

public static class WebApplicationExtensions
{
    public static WebApplicationBuilder ConfigureLogging(this WebApplicationBuilder builder)
    {
        builder.Logging.ClearProviders();
        if (builder.Environment.IsDevelopment())
        {
            builder.Logging.AddDebug();
            builder.Logging.AddConsole();
        }
        else
        {
            builder.Logging.AddApplicationInsights();
        }
        return builder;
    }

    public static WebApplicationBuilder ConfigureAppConfiguration(this WebApplicationBuilder builder)
    {
        var appConfigConnectionString = builder.Configuration.GetConnectionString("AppConfigurationConnection");
        if (!String.IsNullOrEmpty(appConfigConnectionString))
        {
            builder.Configuration.AddAzureAppConfiguration(
                options =>
                {
                    options.Connect(appConfigConnectionString)
                        .ConfigureKeyVault(kv => kv.SetCredential(new DefaultAzureCredential()))
                        .Select(KeyFilter.Any, LabelFilter.Null)
                        .Select(KeyFilter.Any, Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"))
                        .Select(KeyFilter.Any, Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME"));
                },
                optional: true
            );
        }

        return builder;
    }

    public static WebApplicationBuilder ConfigureServices(this WebApplicationBuilder builder)
    {
        // Logging
        builder.Services.AddDatabaseDeveloperPageExceptionFilter();
        builder.Services.AddApplicationInsightsTelemetry(options =>
        {
            options.EnableRequestTrackingTelemetryModule = true;
            options.EnableDependencyTrackingTelemetryModule = false;
            options.EnableAppServicesHeartbeatTelemetryModule = false;
            options.EnableHeartbeat = false;
        });
        builder.Services.AddApplicationInsightsTelemetryProcessor<Ignore304NotModifiedResponsesFilter>();
        builder.Services.AddApplicationInsightsTelemetryProcessor<IgnoreSyntheticRequestsFilter>();
        builder.Services.AddApplicationInsightsTelemetryProcessor<IgnoreStaticWebFilesFilter>();

        // Authentication
        builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApp(
                options =>
                {
                    var config = builder.Configuration.GetSection("AzureAd").Get<MicrosoftIdentityOptions>();
                    options.Instance = config.Instance;
                    options.Domain = config.Domain;
                    options.ClientId = config.ClientId;
                    options.TenantId = config.TenantId;
                    options.CallbackPath = config.CallbackPath;
                    options.NonceCookie.IsEssential = true;
                    options.NonceCookie.HttpOnly = false;
                    options.NonceCookie.SecurePolicy = CookieSecurePolicy.Always;
                    options.CorrelationCookie.IsEssential = true;
                    options.CorrelationCookie.HttpOnly = false;
                    options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
                },
                configureCookieAuthenticationOptions: options =>
                {
                    options.SlidingExpiration = true;
                    options.ExpireTimeSpan = TimeSpan.FromDays(1);
                    options.Cookie.IsEssential = true;
                    options.Cookie.HttpOnly = false;
                    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                });

        // Database
        var steamDbConnectionString = builder.Configuration.GetConnectionString("SteamDbConnection");
        if (!String.IsNullOrEmpty(steamDbConnectionString))
        {
            builder.Services.AddDbContext<SteamDbContext>(options =>
            {
                options.EnableSensitiveDataLogging(AppDomain.CurrentDomain.IsDebugBuild());
                options.EnableDetailedErrors(AppDomain.CurrentDomain.IsDebugBuild());
                options.ConfigureWarnings(c => c.Log((RelationalEventId.CommandExecuting, LogLevel.Debug)));
                options.ConfigureWarnings(c => c.Log((RelationalEventId.CommandExecuted, LogLevel.Debug)));
                options.UseSqlServer(steamDbConnectionString, sql =>
                {
                    sql.EnableRetryOnFailure();
                    sql.CommandTimeout(60);
                });
            });
        }

        // Service bus
        var serviceBusConnectionString = builder.Configuration.GetConnectionString("ServiceBusConnection");
        if (!String.IsNullOrEmpty(serviceBusConnectionString))
        {
            builder.Services.AddAzureServiceBus(serviceBusConnectionString);
        }

        // Redis cache
        var redisConnectionString = builder.Configuration.GetConnectionString("RedisConnection");
        if (!String.IsNullOrEmpty(redisConnectionString))
        {
            builder.Services.AddSingleton(x => ConnectionMultiplexer.Connect(redisConnectionString));
            builder.Services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConnectionString;
            });

            builder.Services.AddSingleton<IStatisticsService, RedisStatisticsService>();
        }

        // Web proxies
        builder.Services.AddSingleton<IWebProxyUsageStatisticsService, WebProxyUsageStatisticsService>();
        builder.Services.AddSingleton<IWebProxyManager>(x => x.GetRequiredService<RotatingWebProxy>()); // Forward interface requests to our singleton
        builder.Services.AddSingleton<IWebProxy>(x => x.GetRequiredService<RotatingWebProxy>()); // Forward interface requests to our singleton
        builder.Services.AddSingleton<RotatingWebProxy>(); // Boo Elion! (https://github.com/aspnet/DependencyInjection/issues/360)

        // Scheduler
        builder.Services.AddScheduler();

        // 3rd party clients
        builder.Services.AddSingleton(x => builder.Configuration.GetSteamConfiguration());
        builder.Services.AddSingleton(x => builder.Configuration.GetAzureAiConfiguration());
        builder.Services.AddSingleton<IImageAnalysisService>(x => x.GetRequiredService<AzureAiClient>()); // Forward interface requests to our singleton
        builder.Services.AddSingleton<AzureAiClient>(); // Boo Elion! (https://github.com/aspnet/DependencyInjection/issues/360)

        builder.Services.AddScoped<SteamWebApiClient>();
        builder.Services.AddScoped<SteamStoreWebClient>();
        builder.Services.AddScoped<SteamCommunityWebClient>();
        builder.Services.AddScoped<ISteamConsoleClient, SteamCmdProcessWrapper>();

        // Command/query/message handlers
        builder.Services.AddCommands(
            AppDomain.CurrentDomain.GetAssemblies()
                .Where(x => x.GetConcreteTypesAssignableTo(typeof(ICommandHandler<>)).Any() || x.GetConcreteTypesAssignableTo(typeof(ICommandHandler<,>)).Any())
                .ToArray()
        );
        builder.Services.AddQueries(
            AppDomain.CurrentDomain.GetAssemblies()
                .Where(x => x.GetConcreteTypesAssignableTo(typeof(IQueryHandler<,>)).Any())
                .ToArray()
        );
        builder.Services.AddMessages(
            AppDomain.CurrentDomain.GetAssemblies()
                .Where(x => x.GetConcreteTypesAssignableTo(typeof(IMessageHandler<>)).Any())
                .ToArray()
        );

        // Jobs
        AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(x => x.GetConcreteTypesAssignableTo(typeof(IJob)))
            .ToList()
            .ForEach(x => builder.Services.AddTransient(x));

        // Controllers
        builder.Services
            .AddControllersWithViews(options =>
            {
                var policy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .Build();

                options.Filters.Add(new AuthorizeFilter(policy));
            })
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.UseDefaults();
            });

        // Views
        builder.Services.AddRazorPages()
             .AddMicrosoftIdentityUI();

        return builder;
    }

    public static WebApplication Configure(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.UseDevelopmentExceptionHandler();
            // Enable automatic DB migrations
            app.UseMigrationsEndPoint();
        }
        else
        {
            app.UseProductionExceptionHandler();
            // Force HTTPS using HSTS
            app.UseHsts();
        }

        app.UseHttpsRedirection();

        app.UseRouting();

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapStaticAssets();

        app.MapControllerRoute(
            name: "default",
            pattern: "{controller=Home}/{action=Index}/{id?}"
        );

        app.MapRazorPages();

        app.UseAzureServiceBusProcessor();

        app.Services.UseScheduler(scheduler =>
        {
            var jobTypes = AppDomain.CurrentDomain.GetAssemblies().SelectMany(x => x.GetConcreteTypesAssignableTo(typeof(IJob)));
            foreach (var jobType in jobTypes)
            {
                var jobAttribute = jobType.GetCustomAttribute<JobAttribute>();
                var jobSchedule = scheduler.ScheduleInvocableType(jobType);
                if (jobAttribute.EverySeconds > 0)
                {
                    jobSchedule.EverySeconds(jobAttribute.EverySeconds.Value)
                        .PreventOverlapping(jobAttribute.Name);
                }
                else if (!String.IsNullOrEmpty(jobAttribute.CronSchedule))
                {
                    jobSchedule.Cron(jobAttribute.CronSchedule)
                        .Zoned(TimeZoneInfo.Local)
                        .PreventOverlapping(jobAttribute.Name);
                }
            }
        })
        .OnError(e =>
        {
            app.Services.GetRequiredService<ILogger<Program>>().LogError(e, "Error occurred in scheduler");
        });

        return app;
    }

    public static WebApplication Warmup(this WebApplication app)
    {
        // Prime caches
        using (var scope = app.Services.CreateScope())
        {
            Task.WaitAll(
                scope.ServiceProvider.GetRequiredService<IWebProxyManager>().RefreshProxiesAsync()
            );
        }

        return app;
    }
}
