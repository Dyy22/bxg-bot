using Newtonsoft.Json;
using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.RegularExpressions;

namespace NadekoBot.Services.Database.Models
{
    public class CustomReaction : DbEntity
    {
        public ulong? GuildId { get; set; }
        public string Response { get; set; }
        public string Trigger { get; set; }

        public bool AutoDeleteTrigger { get; set; }
        public bool DmResponse { get; set; }
        public bool ContainsAnywhere { get; set; }
        public bool AllowTarget { get; set; }
        public string Reactions { get; set; }

        public string[] GetReactions() =>
            string.IsNullOrWhiteSpace(Reactions)
                ? Array.Empty<string>()
                : Reactions.Split("@@@");
        
        public bool IsGlobal() => GuildId is null || GuildId == 0;
    }

    public class ReactionResponse : DbEntity
    {
        public bool OwnerOnly { get; set; }
        public string Text { get; set; }
    }
}
