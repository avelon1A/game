using System;
using System.Collections.Generic;

namespace Veil.Sim
{
    /// <summary>
    /// Reasons to come back (shared by client and server so numbers never disagree):
    /// ranked tiers from the rating, hero levels from playing each hero, and three daily missions that pay coins.
    /// </summary>
    public static class Progression
    {
        // ------------------------------------------------------------------ ranked tiers (from the rating, start 1000)

        public static readonly (string name, int min, string color)[] Tiers =
        {
            ("BRONZE", 0, "#c98a55"), ("SILVER", 950, "#c9d2e0"), ("GOLD", 1100, "#ffd84a"),
            ("PLATINUM", 1250, "#5fe0d0"), ("DIAMOND", 1400, "#8fb8ff"), ("MASTER", 1600, "#c78cff"),
        };

        public static int TierIndex(int rating)
        {
            int t = 0;
            for (int i = 0; i < Tiers.Length; i++) if (rating >= Tiers[i].min) t = i;
            return t;
        }

        public static string TierName(int rating) => Tiers[TierIndex(rating)].name;
        public static string TierColor(int rating) => Tiers[TierIndex(rating)].color;

        /// <summary>0..1 progress from this tier's floor to the next one (1 at the top tier).</summary>
        public static float TierProgress(int rating)
        {
            int t = TierIndex(rating);
            if (t >= Tiers.Length - 1) return 1f;
            return MathUtil.Clamp01((rating - Tiers[t].min) / (float)(Tiers[t + 1].min - Tiers[t].min));
        }

        // ------------------------------------------------------------------ hero levels

        public const int HeroXpPerLevel = 400, HeroMaxLevel = 10, HeroLevelCoins = 100;

        public static int HeroLevel(int xp) => Math.Min(HeroMaxLevel, 1 + xp / HeroXpPerLevel);
        public static float HeroLevelProgress(int xp) => HeroLevel(xp) >= HeroMaxLevel ? 1f : (xp % HeroXpPerLevel) / (float)HeroXpPerLevel;
        public static int HeroMatchXp(PlayerResult r) => 60 + Math.Min(200, r.Total / 20) + (r.SquadRank == 1 ? 60 : 0);

        /// <summary>"outfit:xp,outfit:xp" ↔ dictionary.</summary>
        public static Dictionary<int, int> ParseHeroXp(string s)
        {
            var d = new Dictionary<int, int>();
            if (string.IsNullOrEmpty(s)) return d;
            foreach (var part in s.Split(','))
            {
                var kv = part.Split(':');
                if (kv.Length == 2 && int.TryParse(kv[0], out var h) && int.TryParse(kv[1], out var x)) d[h] = x;
            }
            return d;
        }

        public static string JoinHeroXp(Dictionary<int, int> d)
        {
            var parts = new List<string>();
            foreach (var kv in d) parts.Add(kv.Key + ":" + kv.Value);
            return string.Join(",", parts);
        }

        // ------------------------------------------------------------------ daily missions

        public enum MissionKind : byte { Play, Elims, Win, Top2, Revives, Assists, Score }

        public sealed class Mission
        {
            public MissionKind Kind;
            public int Target, Reward;
            public string Title;
        }

        private static readonly Mission[] Pool =
        {
            new Mission { Kind = MissionKind.Play, Target = 3, Reward = 120, Title = "Play 3 matches" },
            new Mission { Kind = MissionKind.Elims, Target = 8, Reward = 150, Title = "Get 8 eliminations" },
            new Mission { Kind = MissionKind.Elims, Target = 15, Reward = 220, Title = "Get 15 eliminations" },
            new Mission { Kind = MissionKind.Win, Target = 1, Reward = 250, Title = "Win a match (extract)" },
            new Mission { Kind = MissionKind.Top2, Target = 2, Reward = 160, Title = "Finish top 2 twice" },
            new Mission { Kind = MissionKind.Revives, Target = 3, Reward = 150, Title = "Redeploy 3 squadmates" },
            new Mission { Kind = MissionKind.Assists, Target = 5, Reward = 130, Title = "Get 5 assists" },
            new Mission { Kind = MissionKind.Score, Target = 2500, Reward = 180, Title = "Score 2,500 points" },
        };

        public const int DailyCount = 3;

        public static int DayIndex(DateTime utc) => (int)(utc.Date - new DateTime(2026, 1, 1)).TotalDays;
        public static int Today => DayIndex(DateTime.UtcNow);

        /// <summary>The same three missions for everyone on a given day.</summary>
        public static Mission[] DailyMissions(int day)
        {
            var rng = new Rng(9173 + day * 31);
            var idx = new List<int>();
            for (int i = 0; i < Pool.Length; i++) idx.Add(i);
            var pick = new Mission[DailyCount];
            for (int i = 0; i < DailyCount; i++)
            {
                int k = rng.Int(idx.Count);
                pick[i] = Pool[idx[k]];
                // never two of the same kind on one day
                var kind = Pool[idx[k]].Kind;
                idx.RemoveAll(j => Pool[j].Kind == kind);
                if (idx.Count == 0) break;
            }
            return pick;
        }

        public static int MissionGain(Mission m, PlayerResult r)
        {
            switch (m.Kind)
            {
                case MissionKind.Play: return 1;
                case MissionKind.Elims: return r.Elims;
                case MissionKind.Win: return r.SquadRank == 1 ? 1 : 0;
                case MissionKind.Top2: return r.SquadRank <= 2 ? 1 : 0;
                case MissionKind.Revives: return r.Revives;
                case MissionKind.Assists: return r.Assists;
                default: return Math.Max(0, r.Total);
            }
        }

        /// <summary>Stored as "day|p0,p1,p2". Progress from another day counts as zero.</summary>
        public static int[] ParseMissions(string s, int day)
        {
            var p = new int[DailyCount];
            if (string.IsNullOrEmpty(s)) return p;
            var halves = s.Split('|');
            if (halves.Length != 2 || !int.TryParse(halves[0], out var d) || d != day) return p;
            var parts = halves[1].Split(',');
            for (int i = 0; i < DailyCount && i < parts.Length; i++) int.TryParse(parts[i], out p[i]);
            return p;
        }

        public static string JoinMissions(int day, int[] p) => day + "|" + string.Join(",", p);

        /// <summary>Applies one match: returns the new stored string and the coins earned by missions completed just now.</summary>
        public static string ApplyMatch(string stored, PlayerResult r, int day, out int coins, out int completed)
        {
            var ms = DailyMissions(day);
            var p = ParseMissions(stored, day);
            coins = 0; completed = 0;
            for (int i = 0; i < ms.Length && ms[i] != null; i++)
            {
                int before = p[i];
                p[i] = Math.Min(ms[i].Target, p[i] + MissionGain(ms[i], r));
                if (before < ms[i].Target && p[i] >= ms[i].Target) { coins += ms[i].Reward; completed++; }
            }
            return JoinMissions(day, p);
        }
    }
}
