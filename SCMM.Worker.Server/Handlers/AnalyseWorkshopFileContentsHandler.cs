﻿using CommandQuery;
using SCMM.Azure.ServiceBus;
using SCMM.Azure.ServiceBus.Attributes;
using SCMM.Steam.API.Commands;
using SCMM.Steam.API.Messages;

namespace SCMM.Worker.Server.Handlers
{
    [Concurrency(MaxConcurrentCalls = 1)]
    public class AnalyseWorkshopFileContentsHandler : Worker.Client.WebClient, IMessageHandler<AnalyseWorkshopFileContentsMessage>
    {
        private readonly ICommandProcessor _commandProcessor;

        public AnalyseWorkshopFileContentsHandler(ICommandProcessor commandProcessor)
        {
            _commandProcessor = commandProcessor;
        }

        public async Task HandleAsync(AnalyseWorkshopFileContentsMessage message, MessageContext context)
        {
            await _commandProcessor.ProcessAsync(new AnalyseSteamWorkshopContentsInBlobStorageRequest()
            {
                BlobName = message.BlobName,
                Force = message.Force
            });
        }
    }
}
