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
        public int tag { get; set; }
        public string handle { get; set; }   // Name#1234 — what friends type to add you
    }

    /// <summary>A friend (or request) as shown in the friends list.</summary>
    public sealed class FriendInfo
    {
        public string id { get; set; }
        public string name { get; set; }
        public string handle { get; set; }
        public string appearance { get; set; }
        public int level { get; set; }
        public long requestId { get; set; }
        public string since { get; set; }
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
  PRIMARY KEY(match_id, player_id));
CREATE TABLE IF NOT EXISTS friendships(
  a TEXT NOT NULL, b TEXT NOT NULL, created_at TEXT NOT NULL, PRIMARY KEY(a, b));
CREATE TABLE IF NOT EXISTS friend_requests(
  id INTEGER PRIMARY KEY AUTOINCREMENT, from_id TEXT NOT NULL, to_id TEXT NOT NULL, created_at TEXT NOT NULL,
  UNIQUE(from_id, to_id));");
            AddColumn("players", "tag", "INTEGER NOT NULL DEFAULT 0");
            AddColumn("players", "last_seen", "TEXT NOT NULL DEFAULT ''");
            AddColumn("match_players", "squad", "INTEGER NOT NULL DEFAULT 0");
            AddColumn("players", "google_sub", "TEXT NOT NULL DEFAULT ''");
            AddColumn("players", "email", "TEXT NOT NULL DEFAULT ''");
            Exec("CREATE UNIQUE INDEX IF NOT EXISTS players_google ON players(google_sub) WHERE google_sub <> ''");
            AddColumn("match_players", "squad_rank", "INTEGER NOT NULL DEFAULT 0");
            // older rows: give every player a discriminator so handles are unique
            lock (_lock)
            {
                using var c = Open();
                using var q = c.CreateCommand();
                q.CommandText = "SELECT id FROM players WHERE tag = 0";
                var ids = new List<string>();
                using (var r = q.ExecuteReader()) while (r.Read()) ids.Add(r.GetString(0));
                foreach (var id in ids)
                {
                    using var u = c.CreateCommand();
                    u.CommandText = "UPDATE players SET tag=$t WHERE id=$id";
                    u.Parameters.AddWithValue("$t", RandomNumberGenerator.GetInt32(1000, 10000));
                    u.Parameters.AddWithValue("$id", id);
                    u.ExecuteNonQuery();
                }
            }
        }

        private void AddColumn(string table, string col, string def)
        {
            lock (_lock)
            {
                using var c = Open();
                using var q = c.CreateCommand();
                q.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name='{col}'";
                if (Convert.ToInt32(q.ExecuteScalar()) > 0) return;
                using var a = c.CreateCommand();
                a.CommandText = $"ALTER TABLE {table} ADD COLUMN {col} {def}";
                a.ExecuteNonQuery();
            }
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
                cmd.CommandText = "INSERT INTO players(id, token, name, created_at, tag) VALUES($id, $t, $n, $c, $tag)";
                cmd.Parameters.AddWithValue("$tag", RandomNumberGenerator.GetInt32(1000, 10000));
                cmd.Parameters.AddWithValue("$id", id);
                cmd.Parameters.AddWithValue("$t", token);
                cmd.Parameters.AddWithValue("$n", name);
                cmd.Parameters.AddWithValue("$c", DateTime.UtcNow.ToString("o"));
                cmd.ExecuteNonQuery();
            }
            return (id, token);
        }

        // ---------------- admin dashboard ----------------
        public object AdminStats()
        {
            lock (_lock)
            {
                using var c = Open();
                long Q(string sql, string since = null)
                {
                    using var cmd = c.CreateCommand(); cmd.CommandText = sql;
                    if (since != null) cmd.Parameters.AddWithValue("$s", since);
                    return Convert.ToInt64(cmd.ExecuteScalar());
                }
                string Ago(double hours) => DateTime.UtcNow.AddHours(-hours).ToString("o");
                return new
                {
                    accounts = Q("SELECT COUNT(*) FROM players"),
                    google = Q("SELECT COUNT(*) FROM players WHERE google_sub <> ''"),
                    active24h = Q("SELECT COUNT(*) FROM players WHERE last_seen > $s", Ago(24)),
                    active7d = Q("SELECT COUNT(*) FROM players WHERE last_seen > $s", Ago(24 * 7)),
                    new24h = Q("SELECT COUNT(*) FROM players WHERE created_at > $s", Ago(24)),
                    new7d = Q("SELECT COUNT(*) FROM players WHERE created_at > $s", Ago(24 * 7)),
                    matches = Q("SELECT COUNT(*) FROM matches"),
                    matches24h = Q("SELECT COUNT(*) FROM matches WHERE ended_at > $s", Ago(24)),
                    friendships = Q("SELECT COUNT(*) FROM friendships"),
                };
            }
        }

        public List<object> AdminPlayers(string query, int limit)
        {
            var list = new List<object>();
            lock (_lock)
            {
                using var c = Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "SELECT name, tag, xp, rating, matches, wins, best_score, eliminations, created_at, last_seen, email FROM players " +
                                  "WHERE $q = '' OR name LIKE $like OR (name || '#' || tag) LIKE $like ORDER BY last_seen DESC LIMIT $n";
                cmd.Parameters.AddWithValue("$q", query ?? "");
                cmd.Parameters.AddWithValue("$like", "%" + (query ?? "") + "%");
                cmd.Parameters.AddWithValue("$n", limit);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    int level = LevelForXp(r.GetInt32(2), out _, out _);
                    list.Add(new
                    {
                        handle = r.GetString(0) + "#" + r.GetInt32(1), level, rating = r.GetInt32(3), matches = r.GetInt32(4), wins = r.GetInt32(5),
                        best = r.GetInt32(6), elims = r.GetInt32(7), created = r.GetString(8), lastSeen = r.GetString(9), google = r.GetString(10) != "",
                    });
                }
            }
            return list;
        }

        // ---------------- Google accounts ----------------
        /// <summary>Player id owning this Google account (subject), or null.</summary>
        public string FindByGoogle(string sub) => Scalar("SELECT id FROM players WHERE google_sub=$v AND google_sub <> ''", sub);

        /// <summary>Google subject linked to a player ("" = guest).</summary>
        public string GoogleOf(string id) => Scalar("SELECT google_sub FROM players WHERE id=$v", id) ?? "";
        public string EmailOf(string id) => Scalar("SELECT email FROM players WHERE id=$v", id) ?? "";
        public string TokenOf(string id) => Scalar("SELECT token FROM players WHERE id=$v", id);

        public void LinkGoogle(string id, string sub, string email)
        {
            lock (_lock)
            {
                using var c = Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "UPDATE players SET google_sub=$s, email=$e WHERE id=$id";
                cmd.Parameters.AddWithValue("$s", sub);
                cmd.Parameters.AddWithValue("$e", email ?? "");
                cmd.Parameters.AddWithValue("$id", id);
                cmd.ExecuteNonQuery();
            }
        }

        private string Scalar(string sql, string v)
        {
            lock (_lock)
            {
                using var c = Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("$v", v ?? "");
                return cmd.ExecuteScalar() as string;
            }
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
            string nm = r.GetString(r.GetOrdinal("name"));
            int tag = r.GetInt32(r.GetOrdinal("tag"));
            return new PlayerProfile
            {
                tag = tag, handle = $"{nm}#{tag}",
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

                int squads = Math.Max(2, GameConfig.SquadCount);
                foreach (var res in results)
                {
                    if (!profileByPlayer.TryGetValue(res.PlayerId, out var pid) || string.IsNullOrEmpty(pid)) continue;
                    // team rating: squad placement drives it (K = 32), individual share of the squad score nudges it (±6)
                    float placement = 1f - (res.SquadRank - 1) / (float)(squads - 1);   // 1 = best squad, 0 = last
                    float share = res.SquadTotal > 0 ? res.Total / (float)res.SquadTotal : 0.25f;
                    int delta = (int)MathF.Round(32 * (placement - 0.5f) * 2f + MathUtil.Clamp((share - 0.25f) * 24f, -6f, 6f));
                    int xpGain = 50 + res.Total / 10 + (res.SquadRank == 1 ? 100 : 0);
                    int objectives = (res.PrimaryDone ? 1 : 0) + (res.SecondaryDone ? 1 : 0);
                    using var cmd = c.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = @"UPDATE players SET xp = xp + $xp, rating = MAX(0, rating + $dr), matches = matches + 1,
                        wins = wins + $win, top3 = top3 + $top3, best_score = MAX(best_score, $s), total_score = total_score + $s,
                        eliminations = eliminations + $k, deaths = deaths + $dth, objectives = objectives + $o WHERE id = $id";
                    cmd.Parameters.AddWithValue("$xp", xpGain);
                    cmd.Parameters.AddWithValue("$dr", delta);
                    cmd.Parameters.AddWithValue("$win", res.SquadRank == 1 ? 1 : 0);
                    cmd.Parameters.AddWithValue("$top3", res.SquadRank <= 2 ? 1 : 0);
                    cmd.Parameters.AddWithValue("$s", res.Total);
                    cmd.Parameters.AddWithValue("$k", res.Elims);
                    cmd.Parameters.AddWithValue("$dth", res.Deaths);
                    cmd.Parameters.AddWithValue("$o", objectives);
                    cmd.Parameters.AddWithValue("$id", pid);
                    cmd.ExecuteNonQuery();

                    using var mp = c.CreateCommand();
                    mp.Transaction = tx;
                    mp.CommandText = "INSERT OR REPLACE INTO match_players(match_id, player_id, rank, score, squad, squad_rank) VALUES($m,$p,$r,$s,$sq,$sr)";
                    mp.Parameters.AddWithValue("$sq", res.Squad);
                    mp.Parameters.AddWithValue("$sr", res.SquadRank);
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

        // ---------------- social ----------------

        public void Touch(string id)
        {
            lock (_lock)
            {
                using var c = Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "UPDATE players SET last_seen=$t WHERE id=$id";
                cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("o"));
                cmd.Parameters.AddWithValue("$id", id);
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>"Name#1234" → player id (null if unknown).</summary>
        public string FindByHandle(string handle)
        {
            // forgiving: phone keyboards add spaces, full-width '＃', zero-width characters, autocapitalisation
            var sb = new System.Text.StringBuilder();
            foreach (char ch in (handle ?? "").Normalize(System.Text.NormalizationForm.FormKC))
                if (ch == '#' || ch == '＃' || ch == '♯') sb.Append('#');
                else if (!char.IsWhiteSpace(ch) && !char.IsControl(ch) && System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.Format) sb.Append(ch);
            handle = sb.ToString();
            int hash = handle.LastIndexOf('#');
            lock (_lock)
            {
                using var c = Open();
                using var cmd = c.CreateCommand();
                if (hash > 0 && int.TryParse(handle.Substring(hash + 1), out int tag))
                {
                    cmd.CommandText = "SELECT id FROM players WHERE replace(name, ' ', '') = $n COLLATE NOCASE AND tag = $t LIMIT 1";
                    cmd.Parameters.AddWithValue("$n", handle.Substring(0, hash));
                    cmd.Parameters.AddWithValue("$t", tag);
                    return cmd.ExecuteScalar() as string;
                }
                // name only: fine when exactly one player has it
                cmd.CommandText = "SELECT id FROM players WHERE replace(name, ' ', '') = $n COLLATE NOCASE LIMIT 2";
                cmd.Parameters.AddWithValue("$n", hash < 0 ? handle : handle.Substring(0, Math.Max(0, hash)));
                var ids = new List<string>();
                using (var r = cmd.ExecuteReader()) while (r.Read()) ids.Add(r.GetString(0));
                return ids.Count == 1 ? ids[0] : null;
            }
        }

        private static (string, string) Pair(string x, string y) => string.CompareOrdinal(x, y) < 0 ? (x, y) : (y, x);

        public bool AreFriends(string x, string y)
        {
            var (a, b) = Pair(x, y);
            lock (_lock)
            {
                using var c = Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM friendships WHERE a=$a AND b=$b";
                cmd.Parameters.AddWithValue("$a", a); cmd.Parameters.AddWithValue("$b", b);
                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
        }

        public enum RequestResult { Sent, AlreadyFriends, AlreadySent, BecameFriends, Invalid }

        /// <summary>Sends a friend request. If the other player already asked us, this accepts it instead.</summary>
        public RequestResult SendRequest(string from, string to)
        {
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to) || from == to || !Exists(to)) return RequestResult.Invalid;
            if (AreFriends(from, to)) return RequestResult.AlreadyFriends;
            lock (_lock)
            {
                using var c = Open();
                using (var rev = c.CreateCommand())
                {
                    rev.CommandText = "SELECT id FROM friend_requests WHERE from_id=$t AND to_id=$f";
                    rev.Parameters.AddWithValue("$t", to); rev.Parameters.AddWithValue("$f", from);
                    if (rev.ExecuteScalar() is long rid) { AcceptLocked(c, rid, from); return RequestResult.BecameFriends; }
                }
                using var cmd = c.CreateCommand();
                cmd.CommandText = "INSERT OR IGNORE INTO friend_requests(from_id, to_id, created_at) VALUES($f,$t,$c)";
                cmd.Parameters.AddWithValue("$f", from); cmd.Parameters.AddWithValue("$t", to);
                cmd.Parameters.AddWithValue("$c", DateTime.UtcNow.ToString("o"));
                return cmd.ExecuteNonQuery() > 0 ? RequestResult.Sent : RequestResult.AlreadySent;
            }
        }

        /// <summary>Accepts request <paramref name="requestId"/> addressed to <paramref name="by"/>. Returns the requester's id.</summary>
        public string AcceptRequest(long requestId, string by)
        {
            lock (_lock)
            {
                using var c = Open();
                return AcceptLocked(c, requestId, by);
            }
        }

        private string AcceptLocked(SqliteConnection c, long requestId, string by)
        {
            string from;
            using (var q = c.CreateCommand())
            {
                q.CommandText = "SELECT from_id FROM friend_requests WHERE id=$id AND to_id=$to";
                q.Parameters.AddWithValue("$id", requestId); q.Parameters.AddWithValue("$to", by);
                from = q.ExecuteScalar() as string;
            }
            if (from == null) return null;
            var (a, b) = Pair(from, by);
            using (var ins = c.CreateCommand())
            {
                ins.CommandText = "INSERT OR IGNORE INTO friendships(a, b, created_at) VALUES($a,$b,$c)";
                ins.Parameters.AddWithValue("$a", a); ins.Parameters.AddWithValue("$b", b);
                ins.Parameters.AddWithValue("$c", DateTime.UtcNow.ToString("o"));
                ins.ExecuteNonQuery();
            }
            using (var del = c.CreateCommand())
            {
                del.CommandText = "DELETE FROM friend_requests WHERE (from_id=$x AND to_id=$y) OR (from_id=$y AND to_id=$x)";
                del.Parameters.AddWithValue("$x", from); del.Parameters.AddWithValue("$y", by);
                del.ExecuteNonQuery();
            }
            return from;
        }

        /// <summary>Removes a request the player sent (cancel) or received (decline). Returns the other player's id.</summary>
        public string DeleteRequest(long requestId, string by)
        {
            lock (_lock)
            {
                using var c = Open();
                string other;
                using (var q = c.CreateCommand())
                {
                    q.CommandText = "SELECT CASE WHEN from_id=$by THEN to_id ELSE from_id END FROM friend_requests WHERE id=$id AND (from_id=$by OR to_id=$by)";
                    q.Parameters.AddWithValue("$id", requestId); q.Parameters.AddWithValue("$by", by);
                    other = q.ExecuteScalar() as string;
                }
                if (other == null) return null;
                using var d = c.CreateCommand();
                d.CommandText = "DELETE FROM friend_requests WHERE id=$id";
                d.Parameters.AddWithValue("$id", requestId);
                d.ExecuteNonQuery();
                return other;
            }
        }

        public bool RemoveFriend(string x, string y)
        {
            var (a, b) = Pair(x, y);
            lock (_lock)
            {
                using var c = Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "DELETE FROM friendships WHERE a=$a AND b=$b";
                cmd.Parameters.AddWithValue("$a", a); cmd.Parameters.AddWithValue("$b", b);
                return cmd.ExecuteNonQuery() > 0;
            }
        }

        public List<FriendInfo> Friends(string id) => QueryFriends(@"
SELECT p.id, p.name, p.tag, p.appearance, p.xp, 0, f.created_at FROM friendships f
JOIN players p ON p.id = CASE WHEN f.a=$id THEN f.b ELSE f.a END
WHERE f.a=$id OR f.b=$id ORDER BY p.name COLLATE NOCASE", id);

        public List<FriendInfo> Incoming(string id) => QueryFriends(@"
SELECT p.id, p.name, p.tag, p.appearance, p.xp, r.id, r.created_at FROM friend_requests r
JOIN players p ON p.id = r.from_id WHERE r.to_id=$id ORDER BY r.id DESC", id);

        public List<FriendInfo> Outgoing(string id) => QueryFriends(@"
SELECT p.id, p.name, p.tag, p.appearance, p.xp, r.id, r.created_at FROM friend_requests r
JOIN players p ON p.id = r.to_id WHERE r.from_id=$id ORDER BY r.id DESC", id);

        private List<FriendInfo> QueryFriends(string sql, string id)
        {
            var list = new List<FriendInfo>();
            lock (_lock)
            {
                using var c = Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("$id", id);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    string nm = r.GetString(1);
                    list.Add(new FriendInfo
                    {
                        id = r.GetString(0), name = nm, handle = $"{nm}#{r.GetInt32(2)}", appearance = r.GetString(3),
                        level = LevelForXp(r.GetInt32(4), out _, out _), requestId = r.GetInt64(5), since = r.GetString(6),
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
