using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Extensions;
using NadekoBot.Services;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using NadekoBot.Services.Database.Models;
using System.Threading;
using System.Collections.Concurrent;
using System;
using System.Net.Http;
using NadekoBot.Common;
using Serilog;

namespace NadekoBot.Modules.Administration.Services
{
    public sealed class SelfService : ILateExecutor, IReadyExecutor, INService
    {
        private readonly CommandHandler _cmdHandler;
        private readonly DbService _db;
        private readonly IBotStrings _strings;
        private readonly DiscordSocketClient _client;

        private readonly IBotCredentials _creds;

        private ImmutableDictionary<ulong, IDMChannel> ownerChannels =
            new Dictionary<ulong, IDMChannel>().ToImmutableDictionary();

        private ConcurrentDictionary<ulong?, ConcurrentDictionary<int, Timer>> _autoCommands =
            new ConcurrentDictionary<ulong?, ConcurrentDictionary<int, Timer>>();

        private readonly IImageCache _imgs;
        private readonly IHttpClientFactory _httpFactory;
        private readonly BotConfigService _bss;
        private readonly IPubSub _pubSub;
        private readonly IEmbedBuilderService _eb;

        //keys
        private readonly TypedKey<ActivityPubData> _activitySetKey;
        private readonly TypedKey<bool> _imagesReloadKey;
        private readonly TypedKey<string> _guildLeaveKey;

        public SelfService(
            DiscordSocketClient client,
            CommandHandler cmdHandler,
            DbService db,
            IBotStrings strings,
            IBotCredentials creds,
            IDataCache cache,
            IHttpClientFactory factory,
            BotConfigService bss,
            IPubSub pubSub,
            IEmbedBuilderService eb)
        {
            _cmdHandler = cmdHandler;
            _db = db;
            _strings = strings;
            _client = client;
            _creds = creds;
            _imgs = cache.LocalImages;
            _httpFactory = factory;
            _bss = bss;
            _pubSub = pubSub;
            _eb = eb;
            _activitySetKey = new("activity.set");
            _imagesReloadKey = new("images.reload");
            _guildLeaveKey = new("guild.leave");
            
            HandleStatusChanges();
            
            if (_client.ShardId == 0)
            {
                _pubSub.Sub(_imagesReloadKey, async _ => await _imgs.Reload());
            }

            _pubSub.Sub(_guildLeaveKey, async input =>
            {
                var guildStr = input.ToString().Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(guildStr))
                    return;

                var server = _client.Guilds.FirstOrDefault(g => g.Id.ToString() == guildStr
                                                                || g.Name.Trim().ToUpperInvariant() == guildStr);
                if (server is null)
                {
                    return;
                }

                if (server.OwnerId != _client.CurrentUser.Id)
                {
                    await server.LeaveAsync().ConfigureAwait(false);
                    Log.Information($"Left server {server.Name} [{server.Id}]");
                }
                else
                {
                    await server.DeleteAsync().ConfigureAwait(false);
                    Log.Information($"Deleted server {server.Name} [{server.Id}]");
                }
            });
        }

        public async Task OnReadyAsync()
        {
            using var uow = _db.GetDbContext();

            _autoCommands = uow
                .AutoCommands
                .AsNoTracking()
                .Where(x => x.Interval >= 5)
                .AsEnumerable()
                .GroupBy(x => x.GuildId)
                .ToDictionary(x => x.Key,
                    y => y.ToDictionary(x => x.Id, TimerFromAutoCommand)
                        .ToConcurrent())
                .ToConcurrent();

            var startupCommands = uow.AutoCommands.AsNoTracking().Where(x => x.Interval == 0);
            foreach (var cmd in startupCommands)
            {
                try
                {
                    await ExecuteCommand(cmd).ConfigureAwait(false);
                }
                catch
                {
                }
            }

            if (_client.ShardId == 0)
            {
                await LoadOwnerChannels().ConfigureAwait(false);
            }
        }

        private Timer TimerFromAutoCommand(AutoCommand x)
        {
            return new Timer(async (obj) => await ExecuteCommand((AutoCommand) obj).ConfigureAwait(false),
                x,
                x.Interval * 1000,
                x.Interval * 1000);
        }

        private async Task ExecuteCommand(AutoCommand cmd)
        {
            try
            {
                if (cmd.GuildId is null)
                    return;

                var guildShard = (int) ((cmd.GuildId.Value >> 22) % (ulong) _creds.TotalShards);
                if (guildShard != _client.ShardId)
                    return;
                var prefix = _cmdHandler.GetPrefix(cmd.GuildId);
                //if someone already has .die as their startup command, ignore it
                if (cmd.CommandText.StartsWith(prefix + "die", StringComparison.InvariantCulture))
                    return;
                await _cmdHandler.ExecuteExternal(cmd.GuildId, cmd.ChannelId, cmd.CommandText).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error in SelfService ExecuteCommand");
            }
        }

        public void AddNewAutoCommand(AutoCommand cmd)
        {
            using (var uow = _db.GetDbContext())
            {
                uow.AutoCommands.Add(cmd);
                uow.SaveChanges();
            }

            if (cmd.Interval >= 5)
            {
                var autos = _autoCommands.GetOrAdd(cmd.GuildId, new ConcurrentDictionary<int, Timer>());
                autos.AddOrUpdate(cmd.Id, key => TimerFromAutoCommand(cmd), (key, old) =>
                {
                    old.Change(Timeout.Infinite, Timeout.Infinite);
                    return TimerFromAutoCommand(cmd);
                });
            }
        }

        public IEnumerable<AutoCommand> GetStartupCommands()
        {
            using var uow = _db.GetDbContext();
            return uow
                .AutoCommands
                .AsNoTracking()
                .Where(x => x.Interval == 0)
                .OrderBy(x => x.Id)
                .ToList();
        }
        
        public IEnumerable<AutoCommand> GetAutoCommands()
        {
            using var uow = _db.GetDbContext();
            return uow
                .AutoCommands
                .AsNoTracking()
                .Where(x => x.Interval >= 5)
                .OrderBy(x => x.Id)
                .ToList();
        }

        private async Task LoadOwnerChannels()
        {
            var channels = await Task.WhenAll(_creds.OwnerIds.Select(id =>
            {
                var user = _client.GetUser(id);
                if (user is null)
                    return Task.FromResult<IDMChannel>(null);

                return user.GetOrCreateDMChannelAsync();
            })).ConfigureAwait(false);

            ownerChannels = channels.Where(x => x != null)
                .ToDictionary(x => x.Recipient.Id, x => x)
                .ToImmutableDictionary();

            if (!ownerChannels.Any())
                Log.Warning(
                    "No owner channels created! Make sure you've specified the correct OwnerId in the creds.yml file and invited the bot to a Discord server.");
            else
                Log.Information($"Created {ownerChannels.Count} out of {_creds.OwnerIds.Count} owner message channels.");
        }

        public Task LeaveGuild(string guildStr) 
            => _pubSub.Pub(_guildLeaveKey, guildStr);

        // forwards dms
        public async Task LateExecute(IGuild guild, IUserMessage msg)
        {
            var bs = _bss.Data;
            if (msg.Channel is IDMChannel && bs.ForwardMessages && ownerChannels.Any())
            {
                var title = _strings.GetText(strs.dm_from) +
                            $" [{msg.Author}]({msg.Author.Id})";

                var attachamentsTxt = _strings.GetText(strs.attachments);

                var toSend = msg.Content;

                if (msg.Attachments.Count > 0)
                {
                    toSend += $"\n\n{Format.Code(attachamentsTxt)}:\n" +
                              string.Join("\n", msg.Attachments.Select(a => a.ProxyUrl));
                }

                if (bs.ForwardToAllOwners)
                {
                    var allOwnerChannels = ownerChannels.Values;

                    foreach (var ownerCh in allOwnerChannels.Where(ch => ch.Recipient.Id != msg.Author.Id))
                    {
                        try
                        {
                            await ownerCh.SendConfirmAsync(_eb, title, toSend).ConfigureAwait(false);
                        }
                        catch
                        {
                            Log.Warning("Can't contact owner with id {0}", ownerCh.Recipient.Id);
                        }
                    }
                }
                else
                {
                    var firstOwnerChannel = ownerChannels.Values.First();
                    if (firstOwnerChannel.Recipient.Id != msg.Author.Id)
                    {
                        try
                        {
                            await firstOwnerChannel.SendConfirmAsync(_eb, title, toSend).ConfigureAwait(false);
                        }
                        catch
                        {
                            // ignored
                        }
                    }
                }
            }
        }

        public bool RemoveStartupCommand(int index, out AutoCommand cmd)
        {
            using (var uow = _db.GetDbContext())
            {
                cmd = uow.AutoCommands
                    .AsNoTracking()
                    .Where(x => x.Interval == 0)
                    .Skip(index)
                    .FirstOrDefault();

                if (cmd != null)
                {
                    uow.Remove(cmd);
                    uow.SaveChanges();
                    return true;
                }
            }

            return false;
        }
        
        public bool RemoveAutoCommand(int index, out AutoCommand cmd)
        {
            using (var uow = _db.GetDbContext())
            {
                cmd = uow.AutoCommands
                    .AsNoTracking()
                    .Where(x => x.Interval >= 5)
                    .Skip(index)
                    .FirstOrDefault();

                if (cmd != null)
                {
                    uow.Remove(cmd);
                    if (_autoCommands.TryGetValue(cmd.GuildId, out var autos))
                        if (autos.TryRemove(cmd.Id, out var timer))
                            timer.Change(Timeout.Infinite, Timeout.Infinite);
                    uow.SaveChanges();
                    return true;
                }
            }

            return false;
        }

        public async Task<bool> SetAvatar(string img)
        {
            if (string.IsNullOrWhiteSpace(img))
                return false;

            if (!Uri.IsWellFormedUriString(img, UriKind.Absolute))
                return false;

            var uri = new Uri(img);

            using (var http = _httpFactory.CreateClient())
            using (var sr = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
            {
                if (!sr.IsImage())
                    return false;

                // i can't just do ReadAsStreamAsync because dicord.net's image poops itself
                var imgData = await sr.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                using (var imgStream = imgData.ToStream())
                {
                    await _client.CurrentUser.ModifyAsync(u => u.Avatar = new Image(imgStream)).ConfigureAwait(false);
                }
            }

            return true;
        }

        public void ClearStartupCommands()
        {
            using (var uow = _db.GetDbContext())
            {
                var toRemove = uow
                    .AutoCommands
                    .AsNoTracking()
                    .Where(x => x.Interval == 0);

                uow.AutoCommands.RemoveRange(toRemove);
                uow.SaveChanges();
            }
        }

        public Task ReloadImagesAsync() 
            => _pubSub.Pub(_imagesReloadKey, true);

        public bool ForwardMessages()
        {
            var isForwarding = false;
            _bss.ModifyConfig(config => { isForwarding = config.ForwardMessages = !config.ForwardMessages; });

            return isForwarding;
        }

        public bool ForwardToAll()
        {
            var isToAll = false;
            _bss.ModifyConfig(config => { isToAll = config.ForwardToAllOwners = !config.ForwardToAllOwners; });
            return isToAll;
        }
        
        private void HandleStatusChanges()
        {
            _pubSub.Sub(_activitySetKey, async data =>
            {
                try
                {
                    await _client.SetGameAsync(data.Name, data.Link, type: data.Type);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Error setting activity");
                }
            });
        }

        public Task SetGameAsync(string game, ActivityType type) 
            => _pubSub.Pub(_activitySetKey, new() {Name = game, Link = null, Type = type});

        public Task SetStreamAsync(string name, string link)
            => _pubSub.Pub(_activitySetKey, new() { Name = name, Link = link, Type = ActivityType.Streaming });

        private sealed class ActivityPubData
        {
            public string Name { get; init; }
            public string Link { get; init; }
            public ActivityType Type { get; init; }
        }
    }
}