using NadekoBot.Common;
using NadekoBot.Services.Common;
using NadekoBot.Extensions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using NadekoBot.Common.ModuleBehaviors;
using Serilog;

namespace NadekoBot.Services
{
    public sealed class RedisImagesCache : IImageCache, IReadyExecutor
    {
        private readonly ConnectionMultiplexer _con;
        private readonly IBotCredentials _creds;
        private readonly HttpClient _http;

        private IDatabase _db => _con.GetDatabase();

        private const string _basePath = "data/";
        private const string _cardsPath = "data/images/cards";

        public ImageUrls ImageUrls { get; private set; }

        public IReadOnlyList<byte[]> Heads => GetByteArrayData(ImageKey.Coins_Heads);

        public IReadOnlyList<byte[]> Tails => GetByteArrayData(ImageKey.Coins_Tails);

        public IReadOnlyList<byte[]> Dice => GetByteArrayData(ImageKey.Dice);

        public IReadOnlyList<byte[]> SlotEmojis => GetByteArrayData(ImageKey.Slots_Emojis);

        public IReadOnlyList<byte[]> SlotNumbers => GetByteArrayData(ImageKey.Slots_Numbers);

        public IReadOnlyList<byte[]> Currency => GetByteArrayData(ImageKey.Currency);

        public byte[] SlotBackground => GetByteData(ImageKey.Slots_Bg);

        public byte[] RategirlMatrix => GetByteData(ImageKey.Rategirl_Matrix);

        public byte[] RategirlDot => GetByteData(ImageKey.Rategirl_Dot);

        public byte[] XpBackground => GetByteData(ImageKey.Xp_Bg);

        public byte[] Rip => GetByteData(ImageKey.Rip_Bg);

        public byte[] RipOverlay => GetByteData(ImageKey.Rip_Overlay);

        public byte[] GetCard(string key)
        {
            return _con.GetDatabase().StringGet(GetKey("card_" + key));
        }

        public enum ImageKey
        {
            Coins_Heads,
            Coins_Tails,
            Dice,
            Slots_Bg,
            Slots_Numbers,
            Slots_Emojis,
            Rategirl_Matrix,
            Rategirl_Dot,
            Xp_Bg,
            Rip_Bg,
            Rip_Overlay,
            Currency,
        }

        public async Task OnReadyAsync()
        {
            if (await AllKeysExist())
                return;

            await Reload();
        }

        public RedisImagesCache(ConnectionMultiplexer con, IBotCredentials creds)
        {
            _con = con;
            _creds = creds;
            _http = new HttpClient();
            
            ImageUrls = JsonConvert.DeserializeObject<ImageUrls>(
                        File.ReadAllText(Path.Combine(_basePath, "images.json")));
        }

        public async Task<bool> AllKeysExist()
        {
            try
            {
                var results = await Task.WhenAll(Enum.GetNames(typeof(ImageKey))
                    .Select(x => x.ToLowerInvariant())
                    .Select(x => _db.KeyExistsAsync(GetKey(x))))
                    .ConfigureAwait(false);

                var cardsExist = await Task.WhenAll(GetAllCardNames()
                    .Select(x => "card_" + x)
                    .Select(x => _db.KeyExistsAsync(GetKey(x))))
                    .ConfigureAwait(false);

                var num = results.Where(x => !x).Count();

                return results.All(x => x) && cardsExist.All(x => x);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error checking for Image keys");
                return false;
            }
        }

        public async Task Reload()
        {
            try
            {
                var sw = Stopwatch.StartNew();
                var obj = JObject.Parse(
                    File.ReadAllText(Path.Combine(_basePath, "images.json")));

                ImageUrls = obj.ToObject<ImageUrls>();
                var t = new ImageLoader(_http, _con, GetKey)
                    .LoadAsync(obj);

                var loadCards = Task.Run(async () =>
                {
                    await _db.StringSetAsync(Directory.GetFiles(_cardsPath)
                        .ToDictionary(
                            x => GetKey("card_" + Path.GetFileNameWithoutExtension(x)),
                            x => (RedisValue)File.ReadAllBytes(x)) // loads them and creates <name, bytes> pairs to store in redis
                        .ToArray())
                        .ConfigureAwait(false);
                });

                await Task.WhenAll(t, loadCards).ConfigureAwait(false);

                sw.Stop();
                Log.Information($"Images reloaded in {sw.Elapsed.TotalSeconds:F2}s");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error reloading image service");
                throw;
            }
        }

        private IEnumerable<string> GetAllCardNames(bool showExtension = false)
        {
            return Directory.GetFiles(_cardsPath) // gets all cards from the cards folder
                           .Select(x => showExtension
                                ? Path.GetFileName(x)
                                : Path.GetFileNameWithoutExtension(x)); // gets their names
        }

        public RedisKey GetKey(string key)
        {
            return $"{_creds.RedisKey()}_localimg_{key.ToLowerInvariant()}";
        }

        public byte[] GetByteData(string key)
        {
            return _db.StringGet(GetKey(key));
        }

        public byte[] GetByteData(ImageKey key) => GetByteData(key.ToString());

        public RedisImageArray GetByteArrayData(string key)
        {
            return new RedisImageArray(GetKey(key), _con);
        }

        public RedisImageArray GetByteArrayData(ImageKey key) => GetByteArrayData(key.ToString());
    }
}