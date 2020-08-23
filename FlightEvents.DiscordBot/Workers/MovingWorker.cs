using Discord;
using Discord.WebSocket;
using FlightEvents.Data;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FlightEvents.DiscordBot
{
    public class MovingWorker : BackgroundService
    {
        private readonly ILogger<MovingWorker> logger;
        private readonly AppOptions appOptions;
        private readonly DiscordOptions discordOptions;
        private readonly IDiscordConnectionStorage discordConnectionStorage;
        private readonly HubConnection hub;
        private DiscordSocketClient botClient;

        public MovingWorker(ILogger<MovingWorker> logger,
            IOptionsMonitor<AppOptions> appOptionsAccessor,
            IOptionsMonitor<DiscordOptions> discordOptionsAccessor,
            IDiscordConnectionStorage discordConnectionStorage,
            HubConnection hub)
        {
            this.logger = logger;
            this.appOptions = appOptionsAccessor.CurrentValue;
            this.discordOptions = discordOptionsAccessor.CurrentValue;
            this.discordConnectionStorage = discordConnectionStorage;
            this.hub = hub;

            hub.Reconnecting += Hub_Reconnecting;
            hub.Reconnected += Hub_Reconnected;

            hub.On<string, int?, int?>("ChangeFrequency", async (clientId, from, to) =>
            {
                try
                {
                    logger.LogDebug("Got ChangeFrequency message from {clientId} to change from {fromFrequency} to {toFrequency}", clientId, from, to);
                    await CreateVoiceChannelAndMoveAsync(clientId, to);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Cannot handle changing frequency of {0} from {1} to {2}!", clientId, from, to);
                }
            });
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                logger.LogInformation("Bot running at: {time}", DateTimeOffset.Now);

                logger.LogInformation("Connecting to server URL {serverUrl}", appOptions.WebServerUrl);
                await hub.StartAsync();
                logger.LogInformation("Connected to SignalR server");

                try
                {
                    botClient = new DiscordSocketClient();
                    botClient.GuildAvailable += BotClient_GuildAvailable;
                    await botClient.LoginAsync(TokenType.Bot, discordOptions.BotToken);
                    await botClient.StartAsync();
                    logger.LogInformation("Connected to Discord");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Cannot connect to Discord");
                    throw;
                }

                while (!stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(1000, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Cannot initialize bot");
            }
        }

        private Task Hub_Reconnecting(Exception arg)
        {
            logger.LogInformation("Reconnecting to SignalR");
            return Task.CompletedTask;
        }

        private Task Hub_Reconnected(string arg)
        {
            logger.LogInformation("Reconnected to SignalR");
            return Task.CompletedTask;
        }

        private async Task BotClient_GuildAvailable(SocketGuild guild)
        {
            try
            {
                logger.LogInformation("{guildName} is available.", guild.Name);

                var serverOptions = discordOptions.Servers.SingleOrDefault(o => o.ServerId == guild.Id);

                if (serverOptions != null)
                {
                    var category = guild.GetCategoryChannel(serverOptions.ChannelCategoryId);
                    if (category != null)
                    {
                        var lounge = category.Channels.SingleOrDefault(o => o.Name == serverOptions.LoungeChannelName);
                        if (lounge == null)
                        {
                            logger.LogInformation("Create lounge channel named {lounge}.", serverOptions.LoungeChannelName);
                            var newLounge = await guild.CreateVoiceChannelAsync(serverOptions.LoungeChannelName, props =>
                            {
                                props.CategoryId = category.Id;
                            });
                            logger.LogInformation("Created lounge channel {loungeId}", newLounge.Id);
                        }

                    }
                }
            }
            catch(Exception ex)
            {
                logger.LogError(ex, "Cannot prepare server [{guildId}] {guildName}", guild.Id, guild.Name);
            }
        }

        private async Task CreateVoiceChannelAndMoveAsync(string clientId, int? toFrequency)
        {
            var connection = await discordConnectionStorage.GetConnectionAsync(clientId);
            if (connection == null) return;

            SocketGuildUser guildUser = null;
            DiscordServerOptions serverOptions = null;
            foreach (var options in discordOptions.Servers)
            {
                guildUser = botClient.Guilds.SingleOrDefault(o => o.Id == options.ServerId)?.GetUser(connection.UserId);
                serverOptions = options;
                if (guildUser?.VoiceChannel != null) break;
            }

            if (guildUser == null)
            {
                return;
            }

            if (guildUser.VoiceChannel?.CategoryId != serverOptions.ChannelCategoryId)
            {
                // Do not touch user not connecting to voice or connecting outside the channel
                return;
            }

            var guild = guildUser.Guild;

            var channelName = toFrequency.HasValue ?
                CreateChannelNameFromFrequency(serverOptions, toFrequency) :
                serverOptions.LoungeChannelName;

            var channel = guild.Channels.FirstOrDefault(c => c.Name == channelName);
            if (channel == null)
            {
                var voiceChannel = await guild.CreateVoiceChannelAsync(channelName, props =>
                {
                    props.CategoryId = serverOptions.ChannelCategoryId;
                    props.Bitrate = serverOptions.ChannelBitrate;
                });

                try
                {
                    await voiceChannel.AddPermissionOverwriteAsync(guild.EveryoneRole, new OverwritePermissions(useVoiceActivation: PermValue.Deny));
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Cannot change channel permission");
                }

                logger.LogInformation("Created new channel {channelName}", channelName);

                await MoveMemberAsync(guildUser, voiceChannel);
            }
            else
            {
                await MoveMemberAsync(guildUser, channel);
            }
        }

        private static string CreateChannelNameFromFrequency(DiscordServerOptions serverOptions, int? toFrequency)
            => (toFrequency.Value / 1000d).ToString("N3") + (serverOptions.ChannelNameSuffix ?? "");

        private async Task MoveMemberAsync(SocketGuildUser guildUser, IGuildChannel channel)
        {
            await guildUser.ModifyAsync(props =>
            {
                props.ChannelId = channel.Id;
            });
            logger.LogInformation("Moved user {username}#{discriminator} to channel {channelName}", guildUser.Username, guildUser.Discriminator, channel.Name);
        }
    }
}
