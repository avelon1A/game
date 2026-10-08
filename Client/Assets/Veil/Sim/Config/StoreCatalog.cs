using System;
using System.Collections.Generic;

namespace Veil.Sim
{
    /// <summary>
    /// Coins economy (shared by client and server so prices / rules never disagree).
    /// Items are ids like "hero:3" or "gun:2". Everything is cosmetic — no item changes stats.
    /// Gun skins live in Appearance.Accessory (unused by the Meshy heroes): 0 = the standard gun of the chosen type.
    /// </summary>
    public static class StoreCatalog
    {
        public const int StarterCoins = 200;

        public sealed class Item
        {
            public string Id, Name, Desc;
            public int Price;          // 0 = free / owned from the start
            public int Hero = -1;      // Appearance.Outfit for hero items
            public int Skin = -1;      // Appearance.Accessory for gun skins
            public int WeaponType;     // 0 rifle, 1 sniper (gun skins)
        }

        public static readonly Item[] Items =
        {
            new Item { Id = "hero:0", Name = "VANGUARD", Desc = "Starter hero", Price = 0, Hero = 0 },
            new Item { Id = "hero:2", Name = "LYRA", Desc = "Starter hero", Price = 0, Hero = 2 },
            new Item { Id = "hero:1", Name = "VOLT", Desc = "Neon striker", Price = 600, Hero = 1 },
            new Item { Id = "hero:3", Name = "NOVA", Desc = "Pink techwear muse", Price = 800, Hero = 3 },
            new Item { Id = "hero:4", Name = "SOL", Desc = "Solar paladin", Price = 1200, Hero = 4 },
            new Item { Id = "gun:1", Name = "PLASMA RIFLE", Desc = "Rifle skin · gold plasma core", Price = 900, Skin = 1, WeaponType = 0 },
            new Item { Id = "gun:2", Name = "DRAGON SNIPER", Desc = "Sniper skin · dragon scales", Price = 1500, Skin = 2, WeaponType = 1 },
        };

        public static Item Get(string id) { foreach (var i in Items) if (i.Id == id) return i; return null; }
        public static string HeroId(int outfit) => "hero:" + outfit;
        public static string SkinId(int skin) => "gun:" + skin;

        /// <summary>Owned list as stored (comma separated). Free items are always owned.</summary>
        public static HashSet<string> Parse(string owned)
        {
            var set = new HashSet<string>();
            foreach (var i in Items) if (i.Price == 0) set.Add(i.Id);
            if (!string.IsNullOrEmpty(owned))
                foreach (var s in owned.Split(','))
                    if (s.Trim().Length > 0) set.Add(s.Trim());
            return set;
        }

        public static string Join(HashSet<string> owned) => string.Join(",", owned);

        /// <summary>Look string "outfit,hair,hairColor,accessory,color,weapon" (Profile.AppearanceString).</summary>
        public static Appearance ParseLook(string s)
        {
            var a = new Appearance { Outfit = 2 };
            if (string.IsNullOrEmpty(s)) return a;
            var p = s.Split(',');
            byte B(int i) => i < p.Length && byte.TryParse(p[i], out var v) ? v : (byte)0;
            a.Outfit = B(0); a.Hair = B(1); a.HairColor = B(2); a.Accessory = B(3); a.Color = B(4); a.Weapon = B(5);
            return a;
        }

        public static string LookString(Appearance a) => $"{a.Outfit},{a.Hair},{a.HairColor},{a.Accessory},{a.Color},{a.Weapon}";

        /// <summary>Coins for one finished match: playing pays, eliminations and placing well pay more.</summary>
        public static int MatchCoins(int squadRank, int elims, bool extracted)
            => 40 + 5 * Math.Min(elims, 20) + (squadRank == 1 ? 100 : squadRank == 2 ? 50 : squadRank == 3 ? 25 : 10) + (extracted ? 50 : 0);

        /// <summary>A look with anything the player doesn't own swapped for a free default.</summary>
        public static Appearance Sanitize(Appearance look, HashSet<string> owned)
        {
            if (!owned.Contains(HeroId(look.Outfit))) look.Outfit = 2;
            if (look.Accessory != 0)
            {
                var skin = Get(SkinId(look.Accessory));
                if (skin == null || !owned.Contains(skin.Id) || skin.WeaponType != Math.Min((int)look.Weapon, 1)) look.Accessory = 0;
            }
            return look;
        }
    }
}
