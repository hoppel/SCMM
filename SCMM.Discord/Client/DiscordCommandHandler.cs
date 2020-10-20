﻿using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Reflection;
using System.Threading.Tasks;

namespace SCMM.Discord.Client
{
    public class DiscordCommandHandler
    {
        public const char CommandPrefix = '>';

        private readonly ILogger _logger;
        private readonly IServiceProvider _services;
        private readonly CommandService _commands;
        private readonly DiscordSocketClient _client;

        public DiscordCommandHandler(ILogger logger, IServiceProvider services, CommandService commands, DiscordSocketClient client)
        {
            _logger = logger;
            _services = services;
            _commands = commands;
            _client = client;
        }

        public async Task AddCommandsAsync()
        {
            _client.MessageReceived += OnMessageReceivedAsync;
            _commands.CommandExecuted += OnCommandExecutedAsync;
            _commands.Log += OnCommandLogAsync;

            using (var scope = _services.CreateScope())
            {
                await _commands.AddModulesAsync(
                    assembly: Assembly.GetEntryAssembly(),
                    services: scope.ServiceProvider
                );
            }
        }

        private async Task OnMessageReceivedAsync(SocketMessage msg)
        {
            // Don't process the command if it was a system message
            var message = msg as SocketUserMessage;
            if (message == null)
            {
                return;
            }

            // Determine if the message is a command based on the prefix and make sure other bots don't trigger our commands
            int commandArgPos = 0;
            if (!(message.HasCharPrefix(CommandPrefix, ref commandArgPos) ||
                message.HasMentionPrefix(_client.CurrentUser, ref commandArgPos)) ||
                message.Author.IsBot)
            {
                return;
            }

            using (var scope = _services.CreateScope())
            {
                // Execute the command
                var context = new SocketCommandContext(_client, message);
                var result = await _commands.ExecuteAsync(
                    context: context,
                    argPos: commandArgPos,
                    services: scope.ServiceProvider
                );
            }
        }

        public Task OnCommandExecutedAsync(Optional<CommandInfo> command, ICommandContext context, IResult result)
        {
            var commandName = command.IsSpecified ? command.Value.Name : "unspecified";
            if (result.IsSuccess)
            {
                _logger.LogInformation(
                    $"Discord command '{commandName}' executed successfully (guild: {context.Guild.Name}, channel: {context.Channel.Name}, user: {context.User.Username})"
                );
            }
            else if (result.Error != CommandError.UnknownCommand)
            {
                _logger.LogError(
                    $"Discord command '{commandName}' execution failed (guild: {context.Guild.Name}, channel: {context.Channel.Name}, user: {context.User.Username}). Reason: {result.ErrorReason}"
                );
            }

            return Task.CompletedTask;
        }

        public Task OnCommandLogAsync(LogMessage message)
        {
            if (message.Exception is CommandException commandException)
            {
                var command = commandException.Command;
                var context = commandException.Context;
                _logger.LogError(
                    commandException,
                    $"Discord command '{command.Name}' unhandled exception (guild: {context.Guild.Name}, channel: {context.Channel.Name}, user: {context.User.Username})"
                );
            }

            return Task.CompletedTask;
        }
    }
}