using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Veil.Sim;

namespace Veil.Server
{
    public sealed class PlayerProfile
    {
        public string id { get; set; }
        public string name { get; set; }
        public int level { get; set; }
        public int xp { get; set; }
        public int xpToNext { get; set; }
        public int rating { get; set; }
        public int matches { get; set; }
        public int wins { get; set; }
        public int top3 { get; set; }
        public int bestScore { get; set; }
        public int totalScore { get; set; }
        public int avgScore { get; set; }
        public int eliminations { get; set; }
        public int deaths { get; set; }
        public int objectives { get; set; }
        public string appearance { get; set; }
        public string createdAt { get; set; }
    }

    public sealed class MatchSummary
    {
        public long id { get; set; }
        public string endedAt { get; set; }
        public int durationSec { get; set; }
        public int players { get; set; }
        public string winner { get; set; }
        public int winnerScore { get; set; }
        public List<PlayerResult> results { get; set; }
    }

    /// <summary>SQLite persistence for the small test backend. Thread-safe via a single lock.</summary>
    public sealed class Database
    {
        public readonly string Path;
        private readonly string _cs;
        private readonly object _lock = new object();

        public Database(string path)
        {
            Path = path;
            _cs = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
            Exec(@"
CREATE TABLE IF NOT EXISTS players(
  id TEXT PRIMARY KEY, token TEXT NOT NULL, name TEXT NOT NULL, created_at TEXT NOT NULL,
  xp INTEGER NOT NULL DEFAULT 0, rating INTEGER NOT NULL DEFAULT 1000, matches INTEGER NOT NULL DEFAULT 0,
  wins INTEGER NOT NULL DEFAULT 0, top3 INTEGER NOT NULL DEFAULT 0, best_score INTEGER NOT NULL DEFAULT 0,
  total_score INTEGER NOT NULL DEFAULT 0, eliminations INTEGER NOT NULL DEFAULT 0, deaths INTEGER NOT NULL DEFAULT 0,
  objectives INTEGER NOT NULL DEFAULT 0, appearance TEXT NOT NULL DEFAULT '');
CREATE TABLE IF NOT EXISTS matches(
  id INTEGER PRIMARY KEY AUTOINCREMENT, ended_at TEXT NOT NULL, duration_sec INTEGER NOT NULL,
  players INTEGER NOT NULL, winner TEXT NOT NULL, winner_score INTEGER NOT NULL, results_json TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS match_players(
  match_id INTEGER NOT NULL, player_id TEXT NOT NULL, rank INTEGER NOT NULL, score INTEGER NOT NULL,
  PRIMARY KEY(match_id, player_id));");
        }

        private SqliteConnection Open()
        {
            var c = new SqliteConnection(_cs);
            c.Open();
            return c;
        }

        private void Exec(string sql)
        {
            lock (_lock)
            {
                using var c = Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
            }
        }

        // ---------------- progression ----------------
        public static int LevelForXp(int xp, out int xpIntoLevel, out int xpForLevel)
        {
            int level = 1, need = 300;
            while (xp >= need) { xp -= need; level++; need = 300 + (level - 1) * 150; }
            xpIntoLevel = xp;
            xpForLevel = need;
            return level;
        }

        // ---------------- players ----------------
        public (string id, string token) Register(string name)
        {
            name = Sanitize(name);
            string id = Guid.NewGuid().ToString("N").Substring(0, 12);
            string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            lock (_lock)
            {
                using var c = Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "INSERT INTO players(id, token, name, created_at) VALUES($id, $t, $n, $c)";
                cmd.Parameters.AddWithValue("$id", id);
                cmd.Parameters.AddWithValue("$t", token);
                cmd.Parameters.AddWithValue("$n", name);
                cmd.Parameters.AddWithValue("$c", DateTime.UtcNow.ToString("o"));
                cmd.ExecuteNonQuery();
            }
            return (id, token);
        }

        public bool CheckToken(string id, string token)
        {
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(token)) return false;
            lock (_lock)
            {
                using var c = Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "SELECT token FROM players WHERE id=$id";
                cmd.Parameters.AddWithValue("$id", id);
                var t = cmd.ExecuteScalar() as string;
                return t != null && CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(t), System.Text.Encoding.UTF8.GetBytes(token));
            }
        }

        public bool Exists(string id)
        {
            lock (_lock)
            {
                using var c = Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM players WHERE id=$id";
                cmd.Parameters.AddWithValue("$id", id ?? "");
                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
        }

        public void UpdateProfile(string id, string name, string appearance)
        {
            lock (_lock)
            {
                using var c = Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "UPDATE players SET name=COALESCE($n, name), appearance=COALESCE($a, appearance) WHERE id=$id";
                cmd.Parameters.AddWithValue("$id", id);
                cmd.Parameters.AddWithValue("$n", name != null ? Sanitize(name) : (object)DBNull.Value);
                cmd.Parameters.AddWithValue("$a", appearance ?? (object)DBNull.Value);
                cmd.ExecuteNonQuery();
            }
        }

        public PlayerProfile Get(string id)
        {
            lock (_lock)
            {
                using var c = Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "SELECT * FROM players WHERE id=$id";
                cmd.Parameters.AddWithValue("$id", id ?? "");
                using var r = cmd.ExecuteReader();
                return r.Read() ? ReadProfile(r) : null;
            }
        }

        public List<PlayerProfile> Leaderboard(int limit)
        {
            var list = new List<PlayerProfile>();
            lock (_lock)
            {
                using var c = Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "SELECT * FROM players WHERE matches > 0 ORDER BY rating DESC, wins DESC LIMIT $l";
                cmd.Parameters.AddWithValue("$l", limit);
                using var r = cmd.ExecuteReader();
                while (r.Read()) list.Add(ReadProfile(r));
            }
            return list;
        }

        private static PlayerProfile ReadProfile(SqliteDataReader r)
        {
            int xp = r.GetInt32(r.GetOrdinal("xp"));
            int level = LevelForXp(xp, out int into, out int need);
            int matches = r.GetInt32(r.GetOrdinal("matches"));
            int total = r.GetInt32(r.GetOrdinal("total_score"));
            return new PlayerProfile
            {
                id = r.GetString(r.GetOrdinal("id")),
                name = r.GetString(r.GetOrdinal("name")),
                createdAt = r.GetString(r.GetOrdinal("created_at")),
                xp = into, xpToNext = need, level = level,
                rating = r.GetInt32(r.GetOrdinal("rating")),
                matches = matches,
                wins = r.GetInt32(r.GetOrdinal("wins")),
                top3 = r.GetInt32(r.GetOrdinal("top3")),
                bestScore = r.GetInt32(r.GetOrdinal("best_score")),
                totalScore = total,
                avgScore = matches > 0 ? total / matches : 0,
                eliminations = r.GetInt32(r.GetOrdinal("eliminations")),
                deaths = r.GetInt32(r.GetOrdinal("deaths")),
                objectives = r.GetInt32(r.GetOrdinal("objectives")),
                appearance = r.GetString(r.GetOrdinal("appearance")),
            };
        }

        // ---------------- matches ----------------

        /// <summary>Stores a finished match and updates XP / rating for every registered human.</summary>
        public long RecordMatch(int durationSec, List<PlayerResult> results, Dictionary<int, string> profileByPlayer)
        {
            if (results == null || results.Count == 0) return -1;
            lock (_lock)
            {
                using var c = Open();
                using var tx = c.BeginTransaction();
                long matchId;
                using (var cmd = c.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "INSERT INTO matches(ended_at, duration_sec, players, winner, winner_score, results_json) VALUES($e,$d,$p,$w,$ws,$j); SELECT last_insert_rowid();";
                    cmd.Parameters.AddWithValue("$e", DateTime.UtcNow.ToString("o"));
                    cmd.Parameters.AddWithValue("$d", durationSec);
                    cmd.Parameters.AddWithValue("$p", results.Count);
                    cmd.Parameters.AddWithValue("$w", results[0].Name);
                    cmd.Parameters.AddWithValue("$ws", results[0].Total);
                    cmd.Parameters.AddWithValue("$j", JsonSerializer.Serialize(results, new JsonSerializerOptions { IncludeFields = true }));
                    matchId = (long)cmd.ExecuteScalar();
                }

                int n = results.Count;
                foreach (var res in results)
                {
                    if (!profileByPlayer.TryGetValue(res.PlayerId, out var pid) || string.IsNullOrEmpty(pid)) continue;
                    // placement-based Elo-ish rating: expected mid-table, K = 32
                    float placement = n > 1 ? 1f - (res.Rank - 1) / (float)(n - 1) : 1f; // 1 = first, 0 = last
                    int delta = (int)MathF.Round(32 * (placement - 0.5f) * 2f);
                    int xpGain = 50 + res.Total / 10 + (res.Rank == 1 ? 100 : 0);
                    int objectives = (res.PrimaryDone ? 1 : 0) + (res.SecondaryDone ? 1 : 0);
                    using var cmd = c.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = @"UPDATE players SET xp = xp + $xp, rating = MAX(0, rating + $dr), matches = matches + 1,
                        wins = wins + $win, top3 = top3 + $top3, best_score = MAX(best_score, $s), total_score = total_score + $s,
                        eliminations = eliminations + $k, deaths = deaths + $dth, objectives = objectives + $o WHERE id = $id";
                    cmd.Parameters.AddWithValue("$xp", xpGain);
                    cmd.Parameters.AddWithValue("$dr", delta);
                    cmd.Parameters.AddWithValue("$win", res.Rank == 1 ? 1 : 0);
                    cmd.Parameters.AddWithValue("$top3", res.Rank <= 3 ? 1 : 0);
                    cmd.Parameters.AddWithValue("$s", res.Total);
                    cmd.Parameters.AddWithValue("$k", res.Elims);
                    cmd.Parameters.AddWithValue("$dth", res.Deaths);
                    cmd.Parameters.AddWithValue("$o", objectives);
                    cmd.Parameters.AddWithValue("$id", pid);
                    cmd.ExecuteNonQuery();

                    using var mp = c.CreateCommand();
                    mp.Transaction = tx;
                    mp.CommandText = "INSERT OR REPLACE INTO match_players(match_id, player_id, rank, score) VALUES($m,$p,$r,$s)";
                    mp.Parameters.AddWithValue("$m", matchId);
                    mp.Parameters.AddWithValue("$p", pid);
                    mp.Parameters.AddWithValue("$r", res.Rank);
                    mp.Parameters.AddWithValue("$s", res.Total);
                    mp.ExecuteNonQuery();
                }
                tx.Commit();
                return matchId;
            }
        }

        public List<MatchSummary> RecentMatches(int limit)
        {
            var list = new List<MatchSummary>();
            lock (_lock)
            {
                using var c = Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "SELECT id, ended_at, duration_sec, players, winner, winner_score, results_json FROM matches ORDER BY id DESC LIMIT $l";
                cmd.Parameters.AddWithValue("$l", limit);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    list.Add(new MatchSummary
                    {
                        id = r.GetInt64(0), endedAt = r.GetString(1), durationSec = r.GetInt32(2), players = r.GetInt32(3),
                        winner = r.GetString(4), winnerScore = r.GetInt32(5),
                        results = JsonSerializer.Deserialize<List<PlayerResult>>(r.GetString(6), new JsonSerializerOptions { IncludeFields = true }),
                    });
                }
            }
            return list;
        }

        public static string Sanitize(string name)
        {
            name = (name ?? "").Trim();
            var sb = new System.Text.StringBuilder();
            foreach (char ch in name) if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' || ch == ' ') sb.Append(ch);
            var s = sb.ToString().Trim();
            if (s.Length == 0) s = "Player";
            return s.Length > 16 ? s.Substring(0, 16) : s;
        }
    }
}
