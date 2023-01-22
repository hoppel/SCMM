﻿using CommandQuery;
using SCMM.Web.Data.Models.Services;
using SCMM.Web.Data.Models.UI.System;
using SCMM.Web.Server.Queries;

namespace SCMM.Web.Server.Services;

public class CommandQuerySystemService : ISystemService
{
    private readonly IQueryProcessor _queryProcessor;

    public CommandQuerySystemService(IQueryProcessor queryProcessor)
    {
        _queryProcessor = queryProcessor;
    }

    public async Task<SystemStatusDTO> GetSystemStatusAsync(ulong appId, bool includeWebProxiesStatus = false)
    {
        var systemStatus = await _queryProcessor.ProcessAsync(new GetSystemStatusRequest()
        {
            AppId = appId,
            IncludeWebProxies = includeWebProxiesStatus
        });

        return systemStatus?.Status;
    }

    public async Task<IEnumerable<SystemUpdateMessageDTO>> ListLatestSystemUpdateMessagesAsync()
    {
        var latestSystemUpdates = await _queryProcessor.ProcessAsync(new ListLatestSystemUpdateMessagesRequest());
        return latestSystemUpdates?.Messages;
    }
}
