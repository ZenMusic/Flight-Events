﻿using Discord;
using Discord.Rest;
using FlightEvents.Data;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace FlightEvents.Web.Logics
{
    public class DiscordLoginResult
    {
        public DiscordLoginResult(IUser user, string confirmCode)
        {
            User = user;
            ConfirmCode = confirmCode;
        }

        public IUser User { get; }
        public string ConfirmCode { get; }
    }

    public class DiscordLogic
    {
        private readonly HttpClient httpClient;
        private readonly IConfiguration configuration;
        private readonly IDiscordConnectionStorage discordConnectionStorage;
        private static readonly Random random = new Random();

        private static readonly ConcurrentDictionary<string, (DateTimeOffset, RestSelfUser, Tokens)> pendingConnections = new ConcurrentDictionary<string, (DateTimeOffset, RestSelfUser, Tokens)>();

        public DiscordLogic(HttpClient httpClient, IConfiguration configuration, IDiscordConnectionStorage discordConnectionStorage)
        {
            this.httpClient = httpClient;
            this.configuration = configuration;
            this.discordConnectionStorage = discordConnectionStorage;
        }

        public async Task<DiscordLoginResult> LoginAsync(string authCode)
        {
            var response = await httpClient.PostAsync("https://discordapp.com/api/oauth2/token", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = configuration["Discord:ClientId"],
                ["client_secret"] = configuration["Discord:ClientSecret"],
                ["redirect_uri"] = configuration["Discord:RedirectUri"],
                ["grant_type"] = "authorization_code",
                ["scope"] = "identify guilds.join",
                ["code"] = authCode
            }));
            using var stream = await response.Content.ReadAsStreamAsync();
            var tokens = await JsonSerializer.DeserializeAsync<Tokens>(stream);

            var client = new DiscordRestClient();
            await client.LoginAsync(TokenType.Bearer, tokens.access_token);
            var confirmCode = GenerateCode();

            pendingConnections.TryAdd(confirmCode, (DateTimeOffset.Now, client.CurrentUser, tokens));

            return new DiscordLoginResult(client.CurrentUser, confirmCode);
        }

        public async Task ConfirmAsync(string clientId, string code)
        {
            if (pendingConnections.TryGetValue(code, out var value))
            {
                var user = value.Item2;
                var tokens = value.Item3;

                await discordConnectionStorage.StoreConnectionAsync(clientId, user.Id);

                var discordClient = new DiscordRestClient();
                await discordClient.LoginAsync(TokenType.Bearer, tokens.access_token);

                ulong guildId = ulong.Parse(configuration["Discord:ServerId"]);

                var botClient = new DiscordRestClient();
                await botClient.LoginAsync(TokenType.Bot, configuration["Discord:BotToken"]);
                var guild = await botClient.GetGuildAsync(guildId);
                await guild.AddGuildUserAsync(discordClient.CurrentUser.Id, tokens.access_token);
            }
        }

        public async Task ChangeChannelAsync(string clientId, int frequency)
        {
            ulong guildId = ulong.Parse(configuration["Discord:ServerId"]);
            var userId = await discordConnectionStorage.GetUserIdAsync(clientId);
            if (userId == null) return;

            var channelName = (frequency / 1000d).ToString("N3");

            var botClient = new DiscordRestClient();
            await botClient.LoginAsync(TokenType.Bot, configuration["Discord:BotToken"]);
            var guild = await botClient.GetGuildAsync(guildId);
            var channels = await guild.GetVoiceChannelsAsync();
            var guildUser = await botClient.GetGuildUserAsync(guildId, userId.Value);

            var channel = channels.FirstOrDefault(c => c.Name == channelName);
            if (channel == null)
            {
                channel = await guild.CreateVoiceChannelAsync(channelName, props =>
                {
                    props.CategoryId = ulong.Parse(configuration["Discord:ChannelCategoryId"]);
                    props.Bitrate = int.Parse(configuration["Discord:ChannelBitrate"]);
                });
            }

            await guildUser.ModifyAsync(props =>
            {
                props.ChannelId = channel.Id;
            });
        }

        private string GenerateCode()
        {
            var builder = new StringBuilder();
            for (var i = 0; i < 6; i++)
            {
                builder.Append((char)('A' + (char)random.Next(26)));
            }
            return builder.ToString();
        }
    }

    public class Tokens
    {
        public string access_token { get; set; }
        public int expires_in { get; set; }
        public string refresh_token { get; set; }
        public string scope { get; set; }
        public string token_type { get; set; }
    }
}
