using Discord;
using Discord.Commands;
using Discord.WebSocket;
using NadekoBot.Extensions;
using NadekoBot.Services;
using System.Threading.Tasks;
using NadekoBot.Common.Attributes;
using NadekoBot.Modules.Games.Common.Trivia;
using NadekoBot.Modules.Games.Services;
using NadekoBot.Common;

namespace NadekoBot.Modules.Games
{
    public partial class Games
    {
        [Group]
        public class TriviaCommands : NadekoSubmodule<GamesService>
        {
            private readonly IDataCache _cache;
            private readonly ICurrencyService _cs;
            private readonly GamesConfigService _gamesConfig;
            private readonly DiscordSocketClient _client;

            public TriviaCommands(DiscordSocketClient client, IDataCache cache, ICurrencyService cs,
                GamesConfigService gamesConfig)
            {
                _cache = cache;
                _cs = cs;
                _gamesConfig = gamesConfig;
                _client = client;
            }

            [NadekoCommand, Aliases]
            [RequireContext(ContextType.Guild)]
            [Priority(0)]
            [NadekoOptionsAttribute(typeof(TriviaOptions))]
            public Task Trivia(params string[] args)
                => InternalTrivia(args);

            public async Task InternalTrivia(params string[] args)
            {
                var channel = (ITextChannel)ctx.Channel;

                var (opts, _) = OptionsParser.ParseFrom(new TriviaOptions(), args);

                var config = _gamesConfig.Data;
                if (config.Trivia.MinimumWinReq > 0 && config.Trivia.MinimumWinReq > opts.WinRequirement)
                {
                    return;
                }

                var trivia = new TriviaGame(Strings, _client, config, _cache, _cs, channel.Guild, channel, opts,
                    Prefix + "tq", _eb);
                if (_service.RunningTrivias.TryAdd(channel.Guild.Id, trivia))
                {
                    try
                    {
                        await trivia.StartGame().ConfigureAwait(false);
                    }
                    finally
                    {
                        _service.RunningTrivias.TryRemove(channel.Guild.Id, out trivia);
                        await trivia.EnsureStopped().ConfigureAwait(false);
                    }
                    return;
                }

                await SendErrorAsync(GetText(strs.trivia_already_running) + "\n" + trivia.CurrentQuestion)
                    .ConfigureAwait(false);
            }

            [NadekoCommand, Aliases]
            [RequireContext(ContextType.Guild)]
            public async Task Tl()
            {
                if (_service.RunningTrivias.TryGetValue(ctx.Guild.Id, out TriviaGame trivia))
                {
                    await SendConfirmAsync(GetText(strs.leaderboard), trivia.GetLeaderboard()).ConfigureAwait(false);
                    return;
                }

                await ReplyErrorLocalizedAsync(strs.trivia_none).ConfigureAwait(false);
            }

            [NadekoCommand, Aliases]
            [RequireContext(ContextType.Guild)]
            public async Task Tq()
            {
                var channel = (ITextChannel)ctx.Channel;

                if (_service.RunningTrivias.TryGetValue(channel.Guild.Id, out TriviaGame trivia))
                {
                    await trivia.StopGame().ConfigureAwait(false);
                    return;
                }

                await ReplyErrorLocalizedAsync(strs.trivia_none).ConfigureAwait(false);
            }
        }
    }
}