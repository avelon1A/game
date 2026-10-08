using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Veil.Sim;

namespace Veil.Server
{
    /// <summary>
    /// Presence, friends, parties, invites and matchmaking. All state is guarded by one lock and every change is
    /// pushed to the affected Gateway connections. Today in-process; the same message shapes later go over a bus
    /// (Redis/NATS) when Gateways and match hosts run on separate machines.
    /// </summary>
    public sealed class SocialHub : BackgroundService
    {
        public sealed class Session
        {
            public string Id = "", Name = "", Handle = "", Look = "";
            public int Level = 1;
            public GatewayConnection Conn;
            public DateTime LastSeen = DateTime.UtcNow;
            public DateTime ConnectedAt = DateTime.UtcNow;
            public int PingMs;
            public string PartyId;
            public int MatchId = -1;
            public MatchAssignedMsg Assignment;
            public DateTime MatchEndsUtc;
            public HashSet<string> Friends = new HashSet<string>();
            public bool Online => Conn != null;
        }

        private sealed class Member { public string Id = ""; public bool Ready; }

        private sealed class Party
        {
            public string Id = "", Code = "", Leader = "";
            public readonly List<Member> Members = new List<Member>();
            public PartyPhase Phase = PartyPhase.Idle;
            public DateTime QueuedAt;
            public int Seconds = 900;
            public int MatchId = -1;
        }

        private sealed class Invite { public string Id = "", PartyId = "", From = "", To = ""; public DateTime Expires; }

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true };
        private const string CodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        private readonly object _gate = new object();
        private readonly Dictionary<string, Session> _sessions = new Dictionary<string, Session>();
        private readonly Dictionary<string, Party> _parties = new Dictionary<string, Party>();
        private readonly Dictionary<string, Invite> _invites = new Dictionary<string, Invite>();
        private readonly List<Party> _queue = new List<Party>();
        private readonly Database _db;
        private readonly GameHost _host;
        private readonly TicketSigner _tickets;
        private readonly ServerOptions _opt;
        private readonly ILogger<SocialHub> _log;
        private int _nextMatchId = 1;

        public SocialHub(Database db, GameHost host, TicketSigner tickets, ServerOptions opt, ILogger<SocialHub> log)
        {
            _db = db; _host = host; _tickets = tickets; _opt = opt; _log = log;
            _host.MatchEnded += OnMatchEnded;
        }

        public int OnlineCount { get { lock (_gate) return _sessions.Values.Count(s => s.Online); } }

        /// <summary>Admin dashboard: who is online, what they are doing, and every party.</summary>
        public object AdminSnapshot()
        {
            lock (_gate)
            {
                var now = DateTime.UtcNow;
                string Status(Session s)
                {
                    if (s.MatchId >= 0 && s.Assignment != null && now < s.MatchEndsUtc) return "in match";
                    var p = PartyOf(s);
                    return p == null ? "lobby" : p.Phase == PartyPhase.Queued ? "searching" : p.Members.Count > 1 ? "in party" : "lobby";
                }
                var online = _sessions.Values.Where(s => s.Online).OrderBy(s => s.ConnectedAt).Select(s => new
                {
                    handle = s.Handle, level = s.Level, ping = s.PingMs, status = Status(s),
                    party = PartyOf(s)?.Code ?? "", minutes = (int)(now - s.ConnectedAt).TotalMinutes,
                }).ToArray();
                var parties = _parties.Values.Where(p => p.Members.Count > 0).Select(p => new
                {
                    code = p.Code, phase = p.Phase.ToString(),
                    queueSeconds = p.Phase == PartyPhase.Queued ? (int)(now - p.QueuedAt).TotalSeconds : 0,
                    members = p.Members.Select(m => _sessions.TryGetValue(m.Id, out var ms) ? ms.Handle : "?").ToArray(),
                    leader = _sessions.TryGetValue(p.Leader, out var ls) ? ls.Handle : "",
                }).ToArray();
                return new { online, parties };
            }
        }
        public int PartyCount { get { lock (_gate) return _parties.Count; } }
        public int QueuedParties { get { lock (_gate) return _queue.Count; } }

        // ================================================================== connections

        /// <summary>Authenticates a Gateway connection. Returns null (and a reason) if refused.</summary>
        public Session Connect(GatewayConnection conn, GwHello hello, out string error)
        {
            error = "";
            string newToken = "";
            if (hello != null && string.IsNullOrEmpty(hello.id))
            {
                // UDP clients have no REST: the first hello creates the guest account
                var (nid, ntok) = _db.Register(string.IsNullOrWhiteSpace(hello.name) ? "Player" : hello.name);
                hello.id = nid; hello.token = ntok; newToken = ntok;
            }
            if (hello == null || !_db.CheckToken(hello.id, hello.token)) { error = "bad credentials"; return null; }
            if (!string.IsNullOrWhiteSpace(hello.name) || !string.IsNullOrEmpty(hello.look)) _db.UpdateProfile(hello.id, string.IsNullOrWhiteSpace(hello.name) ? null : hello.name, string.IsNullOrEmpty(hello.look) ? null : hello.look);
            var prof = _db.Get(hello.id);
            var friends = _db.Friends(hello.id).Select(f => f.id).ToHashSet();
            _db.Touch(hello.id);
            GatewayConnection replaced = null;
            Session s;
            lock (_gate)
            {
                if (!_sessions.TryGetValue(hello.id, out s)) { s = new Session { Id = hello.id }; _sessions[hello.id] = s; }
                if (s.Conn != null && s.Conn != conn) replaced = s.Conn;
                s.Conn = conn;
                s.ConnectedAt = DateTime.UtcNow;
                s.Name = prof.name; s.Handle = prof.handle; s.Look = prof.appearance; s.Level = prof.level;
                s.Friends = friends;
                s.LastSeen = DateTime.UtcNow;
                conn.Session = s;
                Push(s, Gw.Welcome, new GwWelcome { id = s.Id, name = s.Name, handle = s.Handle, serverTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    voicePort = _opt.PublicVoicePort > 0 ? _opt.PublicVoicePort : _opt.VoicePort, voiceHost = _opt.PublicVoiceHost, newToken = newToken,
                    email = _db.EmailOf(hello.id) });
                PushFriends(s);
                if (PartyOf(s) == null) CreateParty(s); else PushParty(s);   // always in a room
                foreach (var inv in _invites.Values.Where(i => i.To == s.Id)) PushInvite(inv);
                if (s.MatchId >= 0 && s.Assignment != null && DateTime.UtcNow < s.MatchEndsUtc)
                {
                    var again = Clone(s.Assignment); again.rejoin = true;
                    Push(s, Gw.MatchAssigned, again);   // app restarted mid-match → offer REJOIN
                }
                BroadcastPresence(s);
            }
            if (replaced != null) { replaced.Session = null; replaced.Close("signed in on another device"); }
            _log.LogInformation("Gateway: {Handle} online", s.Handle);
            return s;
        }

        public void Disconnected(GatewayConnection conn)
        {
            lock (_gate)
            {
                var s = conn.Session;
                if (s == null || s.Conn != conn) return;
                s.Conn = null;
                s.LastSeen = DateTime.UtcNow;
                BroadcastPresence(s);
                if (s.PartyId != null && _parties.TryGetValue(s.PartyId, out var p)) PushPartyAll(p);
                _log.LogInformation("Gateway: {Handle} offline", s.Handle);
            }
        }

        // ================================================================== requests

        public void Handle(GatewayConnection conn, GwEnvelope env)
        {
            var s = conn.Session;
            if (s == null) return;
            if (env.t == Gw.AuthGoogle) { _ = AuthGoogleAsync(conn, s, env); return; }   // network verification: off the lock
            string err = null;
            object reply = null;
            try
            {
                lock (_gate)
                {
                    s.LastSeen = DateTime.UtcNow;
                    switch (env.t)
                    {
                        case Gw.Ping:
                        {
                            var p = D<GwPing>(env);
                            if (p.rttMs > 0 && p.rttMs != s.PingMs)
                            {
                                s.PingMs = p.rttMs;
                                if (s.PartyId != null && _parties.TryGetValue(s.PartyId, out var pp)) PushPartyAll(pp);
                            }
                            Push(s, Gw.Pong, new GwPing { clientMs = p.clientMs });
                            return;
                        }
                        case Gw.FriendList: PushFriends(s); break;
                        case Gw.FriendRequest: err = FriendRequest(s, D<GwText>(env).text); break;
                        case Gw.FriendAccept: err = FriendAccept(s, D<GwRequestId>(env).requestId); break;
                        case Gw.FriendDecline:
                        case Gw.FriendCancel: err = FriendDelete(s, D<GwRequestId>(env).requestId); break;
                        case Gw.FriendRemove: err = FriendRemove(s, D<GwId>(env).id); break;
                        case Gw.PartyCreate: err = CreateParty(s); break;
                        case Gw.PartyJoin: err = JoinByCode(s, D<GwText>(env).text); break;
                        case Gw.PartyLeave: LeaveParty(s, "left"); break;
                        case Gw.PartyInvite: err = InviteFriend(s, D<GwId>(env).id); break;
                        case Gw.InviteAccept: err = AcceptInvite(s, D<GwId>(env).id); break;
                        case Gw.InviteDecline: DeclineInvite(s, D<GwId>(env).id); break;
                        case Gw.PartyReady: err = SetReady(s, D<GwFlag>(env).value); break;
                        case Gw.PartyKick: err = Kick(s, D<GwId>(env).id); break;
                        case Gw.PartyPromote: err = Promote(s, D<GwId>(env).id); break;
                        case Gw.PartyStart: err = StartQueue(s, D<GwStart>(env).seconds); break;
                        case Gw.PartyCancel: err = CancelQueue(s); break;
                        case Gw.PartyLook: err = SetLook(s, D<GwText>(env).text); break;
                        case Gw.ProfileGet: reply = _db.Get(s.Id); break;
                        case Gw.StoreBuy: err = _db.Buy(s.Id, D<GwText>(env).text); if (err == null) reply = _db.Get(s.Id); break;
                        case Gw.LeaderboardGet: reply = new { players = _db.Leaderboard(20) }; break;
                        case Gw.MatchRejoin:
                            if (s.Assignment == null || DateTime.UtcNow >= s.MatchEndsUtc) err = "no match to rejoin";
                            else { var a = Clone(s.Assignment); a.rejoin = true; reply = a; }
                            break;
                        default: err = "unknown request " + env.t; break;
                    }
                }
            }
            catch (Exception e) { err = "bad request"; _log.LogWarning("Gateway {Handle} {T}: {Err}", s.Handle, env.t, e.Message); }
            if (!string.IsNullOrEmpty(env.id))
                conn.Send(new GwEnvelope { t = Gw.Reply, id = env.id, ok = err == null, err = err ?? "", d = reply != null ? JsonSerializer.Serialize(reply, reply.GetType(), Json) : "" });
        }

        // ================================================================== Google sign-in

        /// <summary>
        /// Verifies a Google ID token and links the Google account to this player (guest progress is kept).
        /// If that Google account already belongs to another player, the client is told to switch to it.
        /// </summary>
        private async System.Threading.Tasks.Task AuthGoogleAsync(GatewayConnection conn, Session s, GwEnvelope env)
        {
            string err = null;
            GwAuthResult res = null;
            try
            {
                if (_opt.GoogleClientIds.Length == 0) err = "Google sign-in isn't set up on this server yet";
                else
                {
                    var token = D<GwGoogleAuth>(env).idToken;
                    var p = await Google.Apis.Auth.GoogleJsonWebSignature.ValidateAsync(token,
                        new Google.Apis.Auth.GoogleJsonWebSignature.ValidationSettings { Audience = _opt.GoogleClientIds });
                    string owner = _db.FindByGoogle(p.Subject);
                    string mine = _db.GoogleOf(s.Id);
                    if (owner == s.Id)
                        res = new GwAuthResult { id = s.Id, handle = s.Handle, name = s.Name, email = p.Email };
                    else if (owner != null)
                    {
                        var o = _db.Get(owner);
                        res = new GwAuthResult { id = owner, token = _db.TokenOf(owner), handle = o.handle, name = o.name, email = p.Email, switched = true };
                    }
                    else if (!string.IsNullOrEmpty(mine))
                    {
                        // this player is already linked to a different Google account: give the new one its own player
                        var (nid, ntok) = _db.Register(string.IsNullOrWhiteSpace(p.GivenName) ? "Player" : p.GivenName);
                        _db.LinkGoogle(nid, p.Subject, p.Email);
                        var o = _db.Get(nid);
                        res = new GwAuthResult { id = nid, token = ntok, handle = o.handle, name = o.name, email = p.Email, switched = true };
                    }
                    else
                    {
                        _db.LinkGoogle(s.Id, p.Subject, p.Email);
                        res = new GwAuthResult { id = s.Id, handle = s.Handle, name = s.Name, email = p.Email };
                    }
                    _log.LogInformation("Google sign-in: {Handle} -> {Email} ({Mode})", s.Handle, p.Email, res.switched ? "switch" : "linked");
                }
            }
            catch (Google.Apis.Auth.InvalidJwtException e) { err = "Google sign-in failed (" + e.Message + ")"; }
            catch (Exception e) { err = "Google sign-in failed"; _log.LogWarning("Google sign-in {Handle}: {Err}", s.Handle, e.Message); }
            if (!string.IsNullOrEmpty(env.id))
                conn.Send(new GwEnvelope { t = Gw.Reply, id = env.id, ok = err == null, err = err ?? "", d = res != null ? JsonSerializer.Serialize(res, Json) : "" });
        }

        private static T D<T>(GwEnvelope e) where T : new() => string.IsNullOrEmpty(e.d) ? new T() : JsonSerializer.Deserialize<T>(e.d, Json) ?? new T();

        // ================================================================== friends

        private string FriendRequest(Session s, string handle)
        {
            string to = _db.FindByHandle(handle);
            if (to == null) { _log.LogInformation("Friend search miss: {Q}", string.Join(" ", (handle ?? "").Select(ch => ((int)ch).ToString("x")))); return $"No player called {handle}. Use the full Name#1234."; }
            switch (_db.SendRequest(s.Id, to))
            {
                case Database.RequestResult.Invalid: return "You can't add yourself";
                case Database.RequestResult.AlreadyFriends: return "Already friends";
                case Database.RequestResult.AlreadySent: return "Request already sent";
                case Database.RequestResult.BecameFriends: FriendsChanged(s.Id, to); Notice(s, "You are now friends"); break;
                default: Notice(s, "Friend request sent"); break;
            }
            RefreshFriendLists(s.Id, to);
            if (_sessions.TryGetValue(to, out var t) && t.Online) Notice(t, $"{s.Handle} sent you a friend request");
            return null;
        }

        private string FriendAccept(Session s, long requestId)
        {
            string from = _db.AcceptRequest(requestId, s.Id);
            if (from == null) return "Request not found";
            FriendsChanged(s.Id, from);
            RefreshFriendLists(s.Id, from);
            if (_sessions.TryGetValue(from, out var f) && f.Online) Notice(f, $"{s.Handle} accepted your friend request");
            return null;
        }

        private string FriendDelete(Session s, long requestId)
        {
            string other = _db.DeleteRequest(requestId, s.Id);
            if (other == null) return "Request not found";
            RefreshFriendLists(s.Id, other);
            return null;
        }

        private string FriendRemove(Session s, string other)
        {
            if (!_db.RemoveFriend(s.Id, other)) return "Not friends";
            s.Friends.Remove(other);
            if (_sessions.TryGetValue(other, out var o)) o.Friends.Remove(s.Id);
            RefreshFriendLists(s.Id, other);
            return null;
        }

        private void FriendsChanged(string a, string b)
        {
            if (_sessions.TryGetValue(a, out var sa)) sa.Friends.Add(b);
            if (_sessions.TryGetValue(b, out var sb)) sb.Friends.Add(a);
        }

        private void RefreshFriendLists(params string[] ids)
        {
            foreach (var id in ids)
                if (_sessions.TryGetValue(id, out var x) && x.Online) PushFriends(x);
        }

        private void PushFriends(Session s)
        {
            var st = new FriendsState();
            foreach (var f in _db.Friends(s.Id)) st.friends.Add(Entry(f));
            foreach (var f in _db.Incoming(s.Id)) st.incoming.Add(Entry(f));
            foreach (var f in _db.Outgoing(s.Id)) st.outgoing.Add(Entry(f));
            st.friends.Sort((a, b) => a.status != b.status ? (b.status > 0).CompareTo(a.status > 0) : string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            Push(s, Gw.FriendsState, st);
        }

        private FriendEntry Entry(FriendInfo f)
        {
            var e = new FriendEntry { id = f.id, name = f.name, handle = f.handle, look = f.appearance, level = f.level, requestId = f.requestId };
            FillPresence(e, _sessions.TryGetValue(f.id, out var s) ? s : null);
            return e;
        }

        private void FillPresence(FriendEntry e, Session s)
        {
            var st = StatusOf(s);
            e.status = (int)st;
            e.detail = st switch
            {
                PresenceStatus.InMatch => $"In match · {Math.Max(0, (int)Math.Ceiling((s.MatchEndsUtc - DateTime.UtcNow).TotalMinutes))} min left",
                PresenceStatus.InQueue => "Finding a match",
                PresenceStatus.InParty => $"In a party ({PartyOf(s)?.Members.Count ?? 1}/{GameConfig.SquadSize})",
                PresenceStatus.Online => "Online",
                _ => "Offline",
            };
            var p = PartyOf(s);
            e.joinable = p != null && p.Phase == PartyPhase.Idle && p.Members.Count < GameConfig.SquadSize && s.Online;
            e.partyCode = e.joinable ? p.Code : "";
        }

        private PresenceStatus StatusOf(Session s)
        {
            if (s == null || !s.Online) return s != null && s.MatchId >= 0 ? PresenceStatus.InMatch : PresenceStatus.Offline;
            if (s.MatchId >= 0) return PresenceStatus.InMatch;
            var p = PartyOf(s);
            if (p == null) return PresenceStatus.Online;
            return p.Phase == PartyPhase.Queued ? PresenceStatus.InQueue : PresenceStatus.InParty;
        }

        private void BroadcastPresence(Session s)
        {
            var e = new FriendEntry { id = s.Id, name = s.Name, handle = s.Handle, look = s.Look, level = s.Level };
            FillPresence(e, s);
            foreach (var fid in s.Friends)
                if (_sessions.TryGetValue(fid, out var f) && f.Online) Push(f, Gw.Presence, e);
        }

        // ================================================================== parties

        private Party PartyOf(Session s) => s?.PartyId != null && _parties.TryGetValue(s.PartyId, out var p) ? p : null;

        private string NewCode()
        {
            while (true)
            {
                var c = new string(Enumerable.Range(0, 6).Select(_ => CodeChars[RandomNumberGenerator.GetInt32(CodeChars.Length)]).ToArray());
                if (!_parties.Values.Any(p => p.Code == c)) return c;
            }
        }

        private string CreateParty(Session s)
        {
            var cur = PartyOf(s);
            if (cur != null && cur.Phase != PartyPhase.Idle) return "Your party is busy";
            if (cur != null && cur.Members.Count == 1) { PushParty(s); return null; }   // already alone in a room
            if (cur != null) LeaveParty(s, "left", moving: true);
            var p = new Party { Id = Guid.NewGuid().ToString("N").Substring(0, 10), Code = NewCode(), Leader = s.Id };
            p.Members.Add(new Member { Id = s.Id });
            _parties[p.Id] = p;
            s.PartyId = p.Id;
            PushPartyAll(p);
            BroadcastPresence(s);
            return null;
        }

        private string JoinByCode(Session s, string code)
        {
            code = (code ?? "").Trim().ToUpperInvariant();
            var p = _parties.Values.FirstOrDefault(x => x.Code == code);
            if (p == null) return "Room not found";
            return Join(s, p);
        }

        private string Join(Session s, Party p)
        {
            if (p.Members.Any(m => m.Id == s.Id)) { PushParty(s); return null; }
            if (p.Phase != PartyPhase.Idle) return "That party is already in a match";
            if (p.Members.Count >= GameConfig.SquadSize) return "That party is full";
            var cur = PartyOf(s);
            if (cur != null && cur.Phase != PartyPhase.Idle) return "Leave your current match first";
            if (cur != null) LeaveParty(s, "left", moving: true);   // straight into the new room: no "no room" state in between
            p.Members.Add(new Member { Id = s.Id });
            _log.LogInformation("Party {Code}: {Name} joined ({N}/4)", p.Code, s.Name, p.Members.Count);
            s.PartyId = p.Id;
            foreach (var inv in _invites.Values.Where(i => i.To == s.Id && i.PartyId == p.Id).ToList()) RemoveInvite(inv);
            PushPartyAll(p);
            foreach (var m in p.Members) if (_sessions.TryGetValue(m.Id, out var ms)) BroadcastPresence(ms);
            NoticeParty(p, $"{s.Name} joined the party", s.Id);
            return null;
        }

        /// <summary>Removes a player from their party. Unless they are moving to another room, an online player
        /// immediately gets a fresh solo room — every online player is always in a room (like other squad games).</summary>
        private void LeaveParty(Session s, string why, bool moving = false)
        {
            var p = PartyOf(s);
            s.PartyId = null;
            if (p != null) _log.LogInformation("Party {Code}: {Name} {Why}", p.Code, s.Name, moving ? "moved to another room" : why);
            if (!moving)
            {
                if (s.Online) CreateParty(s);   // pushes the new solo room + presence
                else { PushParty(s); BroadcastPresence(s); }
            }
            if (p == null) return;
            p.Members.RemoveAll(m => m.Id == s.Id);
            if (p.Phase == PartyPhase.Queued) { p.Phase = PartyPhase.Idle; _queue.Remove(p); }
            if (p.Members.Count == 0)
            {
                _parties.Remove(p.Id);
                foreach (var inv in _invites.Values.Where(i => i.PartyId == p.Id).ToList()) RemoveInvite(inv);
                return;
            }
            if (p.Leader == s.Id) p.Leader = p.Members[0].Id;   // leadership passes to the longest-standing member
            PushPartyAll(p);
            foreach (var m in p.Members) if (_sessions.TryGetValue(m.Id, out var ms)) BroadcastPresence(ms);
            NoticeParty(p, why == "kicked" ? $"{s.Name} was removed from the party" : $"{s.Name} left the party", null);
        }

        private string InviteFriend(Session s, string friendId)
        {
            if (!s.Friends.Contains(friendId)) return "You can only invite friends (or share the room code)";
            if (!_sessions.TryGetValue(friendId, out var f) || !f.Online) return "Your friend is offline";
            if (PartyOf(s) == null) { var e = CreateParty(s); if (e != null) return e; }
            var p = PartyOf(s);
            if (p.Members.Any(m => m.Id == friendId)) return "Already in your party";
            if (p.Members.Count >= GameConfig.SquadSize) return "Your party is full";
            if (p.Phase != PartyPhase.Idle) return "Can't invite while in queue or in a match";
            foreach (var old in _invites.Values.Where(i => i.To == friendId && i.PartyId == p.Id).ToList()) RemoveInvite(old);
            var inv = new Invite { Id = Guid.NewGuid().ToString("N").Substring(0, 10), PartyId = p.Id, From = s.Id, To = friendId, Expires = DateTime.UtcNow.AddSeconds(60) };
            _invites[inv.Id] = inv;
            PushInvite(inv);
            PushPartyAll(p);
            Notice(s, $"Invite sent to {f.Name}");
            return null;
        }

        private void PushInvite(Invite inv)
        {
            if (!_sessions.TryGetValue(inv.To, out var to) || !to.Online || !_parties.TryGetValue(inv.PartyId, out var p)) return;
            _sessions.TryGetValue(inv.From, out var from);
            Push(to, Gw.Invite, new IncomingInvite
            {
                inviteId = inv.Id, fromId = inv.From, fromName = from?.Name ?? "?", fromHandle = from?.Handle ?? "", partyCode = p.Code,
                members = p.Members.Count, expiresIn = Math.Max(0, (int)(inv.Expires - DateTime.UtcNow).TotalSeconds),
            });
        }

        private string AcceptInvite(Session s, string inviteId)
        {
            if (!_invites.TryGetValue(inviteId, out var inv) || inv.To != s.Id) return "That invite has expired";
            RemoveInvite(inv);
            if (!_parties.TryGetValue(inv.PartyId, out var p)) return "That party no longer exists";
            return Join(s, p);
        }

        private void DeclineInvite(Session s, string inviteId)
        {
            if (!_invites.TryGetValue(inviteId, out var inv) || inv.To != s.Id) return;
            RemoveInvite(inv);
            if (_sessions.TryGetValue(inv.From, out var from) && from.Online) Notice(from, $"{s.Name} declined your invite");
        }

        private void RemoveInvite(Invite inv)
        {
            _invites.Remove(inv.Id);
            if (_sessions.TryGetValue(inv.To, out var to) && to.Online) Push(to, Gw.InviteGone, new GwId { id = inv.Id });
            if (_parties.TryGetValue(inv.PartyId, out var p)) PushPartyAll(p);
        }

        private string SetReady(Session s, bool ready)
        {
            var p = PartyOf(s);
            if (p == null) return "Not in a party";
            if (p.Phase != PartyPhase.Idle) return "Already in queue";
            p.Members.First(m => m.Id == s.Id).Ready = ready;
            PushPartyAll(p);
            return null;
        }

        private string SetLook(Session s, string look)
        {
            // cosmetics must be owned: anything locked falls back to a free default
            var prof = _db.Get(s.Id);
            if (prof != null) look = StoreCatalog.LookString(StoreCatalog.Sanitize(StoreCatalog.ParseLook(look), StoreCatalog.Parse(prof.owned)));
            s.Look = look ?? "";
            _db.UpdateProfile(s.Id, null, s.Look);
            var p = PartyOf(s);
            if (p != null) PushPartyAll(p);
            return null;
        }

        private string Kick(Session s, string target)
        {
            var p = PartyOf(s);
            if (p == null || p.Leader != s.Id) return "Only the party leader can remove players";
            if (target == s.Id || !p.Members.Any(m => m.Id == target)) return "Not in your party";
            if (p.Phase != PartyPhase.Idle) return "Can't remove players during queue or a match";
            if (_sessions.TryGetValue(target, out var t))
            {
                LeaveParty(t, "kicked");
                if (t.Online) Push(t, Gw.Kicked, new GwText { text = $"{s.Name} removed you from the party" });
            }
            return null;
        }

        private string Promote(Session s, string target)
        {
            var p = PartyOf(s);
            if (p == null || p.Leader != s.Id) return "Only the party leader can transfer leadership";
            if (!p.Members.Any(m => m.Id == target)) return "Not in your party";
            p.Leader = target;
            PushPartyAll(p);
            if (_sessions.TryGetValue(target, out var t)) NoticeParty(p, $"{t.Name} is now the party leader", null);
            return null;
        }

        // ================================================================== matchmaking

        private string StartQueue(Session s, int seconds)
        {
            var p = PartyOf(s);
            if (p == null) { var e = CreateParty(s); if (e != null) return e; p = PartyOf(s); }
            if (p.Leader != s.Id) return "Only the party leader can start";
            if (p.Phase != PartyPhase.Idle) return "Already searching";
            p.Members.First(m => m.Id == s.Id).Ready = true;
            var notReady = p.Members.Where(m => !m.Ready).Select(m => _sessions.TryGetValue(m.Id, out var x) ? x.Name : "?").ToList();
            if (notReady.Count > 0) return "Waiting for: " + string.Join(", ", notReady);
            var offline = p.Members.Where(m => !_sessions.TryGetValue(m.Id, out var x) || !x.Online).ToList();
            if (offline.Count > 0) return "A party member is offline";
            p.Seconds = Math.Clamp(seconds, 60, 900);
            p.Phase = PartyPhase.Queued;
            p.QueuedAt = DateTime.UtcNow;
            _queue.Add(p);
            foreach (var inv in _invites.Values.Where(i => i.PartyId == p.Id).ToList()) RemoveInvite(inv);
            PushPartyAll(p);
            foreach (var m in p.Members) if (_sessions.TryGetValue(m.Id, out var ms)) BroadcastPresence(ms);
            return null;
        }

        private string CancelQueue(Session s)
        {
            var p = PartyOf(s);
            if (p == null || p.Phase != PartyPhase.Queued) return "Not searching";
            p.Phase = PartyPhase.Idle;
            _queue.Remove(p);
            PushPartyAll(p);
            foreach (var m in p.Members) if (_sessions.TryGetValue(m.Id, out var ms)) BroadcastPresence(ms);
            return null;
        }

        /// <summary>
        /// Matchmaker v1: packs queued parties (oldest first) into 4 squads of 4. A match starts as soon as 16 humans
        /// are waiting, or when the oldest party has waited <see cref="ServerOptions.MatchmakingWait"/> seconds — then bots
        /// fill the empty seats. A party is never split across squads. Later: region, MMR bands, backfill.
        /// </summary>
        private void Matchmake()
        {
            if (_queue.Count == 0) return;
            var oldest = _queue.Min(p => p.QueuedAt);
            int humans = _queue.Sum(p => p.Members.Count);
            if (humans < GameConfig.MaxPlayers && (DateTime.UtcNow - oldest).TotalSeconds < _opt.MatchmakingWait) return;

            var room = new int[GameConfig.SquadCount];
            for (int i = 0; i < room.Length; i++) room[i] = GameConfig.SquadSize;
            var picked = new List<(Party party, int squad)>();
            foreach (var p in _queue.OrderBy(x => x.QueuedAt))
            {
                int best = -1;
                for (int sq = 0; sq < room.Length; sq++)   // put the party in the fullest squad that still fits (keeps squads tight)
                    if (room[sq] >= p.Members.Count && (best < 0 || room[sq] < room[best])) best = sq;
                if (best < 0) continue;
                room[best] -= p.Members.Count;
                picked.Add((p, best));
            }
            if (picked.Count == 0) return;

            var req = new MatchRequest { MatchId = _nextMatchId++, Seconds = picked[0].party.Seconds };
            long exp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + req.Seconds + 600;
            foreach (var (p, sq) in picked)
            {
                _queue.Remove(p);
                p.Phase = PartyPhase.InMatch;
                p.MatchId = req.MatchId;
                foreach (var m in p.Members)
                {
                    var s = _sessions[m.Id];
                    req.Seats.Add(new Seat { ProfileId = s.Id, Name = s.Name, Look = ParseLook(s.Look), Squad = sq });
                    s.MatchId = req.MatchId;
                    s.MatchEndsUtc = DateTime.UtcNow.AddSeconds(req.Seconds + 20);
                    string channel = $"match:{req.MatchId}:{sq}";
                    s.Assignment = new MatchAssignedMsg
                    {
                        matchId = req.MatchId, host = _opt.PublicMatchHost, port = _opt.PublicMatchPort > 0 ? _opt.PublicMatchPort : _opt.UdpPort,
                        squad = sq, seconds = req.Seconds,
                        ticket = _tickets.Issue(new TicketData { ProfileId = s.Id, Name = s.Name, MatchId = req.MatchId, Squad = sq, ExpiresUnix = exp }),
                        voiceChannel = channel, voiceToken = VoiceToken(s.Id, channel),
                    };
                    Push(s, Gw.MatchAssigned, s.Assignment);
                }
            }
            _host.Enqueue(req);
            foreach (var (p, _) in picked)
            {
                PushPartyAll(p);
                foreach (var m in p.Members) if (_sessions.TryGetValue(m.Id, out var ms)) BroadcastPresence(ms);
            }
            _log.LogInformation("Matchmaker: match {Id} with {Parties} parties / {Humans} humans", req.MatchId, picked.Count, req.Seats.Count);
        }

        private void OnMatchEnded(int matchId, List<PlayerResult> results, Dictionary<int, string> profiles)
        {
            lock (_gate)
            {
                foreach (var s in _sessions.Values.Where(x => x.MatchId == matchId))
                {
                    s.MatchId = -1;
                    s.Assignment = null;
                    if (s.Online) Push(s, Gw.MatchFinished, new GwId { id = matchId.ToString() });
                }
                foreach (var p in _parties.Values.Where(x => x.MatchId == matchId))
                {
                    p.Phase = PartyPhase.Idle;
                    p.MatchId = -1;
                    foreach (var m in p.Members) m.Ready = false;
                    PushPartyAll(p);
                }
                foreach (var s in _sessions.Values) if (profiles.ContainsValue(s.Id)) { RefreshLevel(s); BroadcastPresence(s); }
            }
        }

        private void RefreshLevel(Session s)
        {
            var prof = _db.Get(s.Id);
            if (prof != null) s.Level = prof.level;
        }

        // ================================================================== housekeeping

        protected override async Task ExecuteAsync(CancellationToken stop)
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    lock (_gate)
                    {
                        Matchmake();
                        var now = DateTime.UtcNow;
                        foreach (var inv in _invites.Values.Where(i => i.Expires < now).ToList()) RemoveInvite(inv);
                        // an offline member of an idle party is removed after 90 s (keeps parties through app restarts)
                        foreach (var s in _sessions.Values.Where(x => !x.Online && x.PartyId != null && (now - x.LastSeen).TotalSeconds > 90).ToList())
                        {
                            var p = PartyOf(s);
                            if (p != null && p.Phase == PartyPhase.Idle) LeaveParty(s, "offline");
                        }
                        foreach (var p in _queue) if ((now - p.QueuedAt).TotalSeconds % 5 < 0.5) PushPartyAll(p);  // queue timer
                    }
                }
                catch (Exception e) { _log.LogError("Hub tick: {Err}", e); }
                await Task.Delay(500, stop).ContinueWith(_ => { });
            }
        }

        // ================================================================== pushes

        private PartyInfo Describe(Party p, Session viewer)
        {
            var info = new PartyInfo
            {
                id = p.Id, code = p.Code, leader = p.Leader, phase = (int)p.Phase, seconds = p.Seconds, matchId = p.MatchId,
                queueSeconds = p.Phase == PartyPhase.Queued ? (int)(DateTime.UtcNow - p.QueuedAt).TotalSeconds : 0,
            };
            foreach (var m in p.Members)
            {
                _sessions.TryGetValue(m.Id, out var s);
                info.members.Add(new PartyMemberInfo
                {
                    id = m.Id, name = s?.Name ?? "?", handle = s?.Handle ?? "", look = s?.Look ?? "", level = s?.Level ?? 1,
                    ready = m.Ready || (p.Leader == m.Id && p.Phase != PartyPhase.Idle), leader = p.Leader == m.Id, online = s?.Online ?? false, ping = s?.PingMs ?? 0,
                });
            }
            foreach (var inv in _invites.Values.Where(i => i.PartyId == p.Id))
                info.pending.Add(new PendingInvite { to = inv.To, name = _sessions.TryGetValue(inv.To, out var t) ? t.Name : "?" });
            info.voiceChannel = $"party:{p.Id}";
            info.voiceToken = VoiceToken(viewer.Id, info.voiceChannel);
            return info;
        }

        private void PushParty(Session s)
        {
            var p = PartyOf(s);
            Push(s, Gw.PartyState, p == null ? new PartyInfo() : Describe(p, s));
        }

        private void PushPartyAll(Party p)
        {
            foreach (var m in p.Members) if (_sessions.TryGetValue(m.Id, out var s) && s.Online) Push(s, Gw.PartyState, Describe(p, s));
        }

        private void Notice(Session s, string text) => Push(s, Gw.Notice, new GwText { text = text });

        private void NoticeParty(Party p, string text, string except)
        {
            foreach (var m in p.Members) if (m.Id != except && _sessions.TryGetValue(m.Id, out var s) && s.Online) Notice(s, text);
        }

        private static void Push(Session s, string type, object payload)
        {
            s.Conn?.Send(new GwEnvelope { t = type, d = JsonSerializer.Serialize(payload, payload.GetType(), Json) });
        }

        /// <summary>Voice relay token: proves the holder may speak/listen on <paramref name="channel"/>.</summary>
        public string VoiceToken(string profileId, string channel) => _tickets.Sign($"voice|{profileId}|{channel}");

        /// <summary>Called by the voice relay: may this profile use this channel right now?</summary>
        public bool MayUseVoice(string profileId, string channel)
        {
            lock (_gate)
            {
                if (!_sessions.TryGetValue(profileId, out var s)) return false;
                if (channel.StartsWith("party:")) return s.PartyId != null && channel == $"party:{s.PartyId}";
                if (channel.StartsWith("match:")) return s.Assignment != null && s.Assignment.voiceChannel == channel;
                return false;
            }
        }

        private static MatchAssignedMsg Clone(MatchAssignedMsg a) => JsonSerializer.Deserialize<MatchAssignedMsg>(JsonSerializer.Serialize(a, Json), Json);

        public static Appearance ParseLook(string s)
        {
            var a = new Appearance();
            var f = (s ?? "").Split(',');
            if (f.Length >= 5 && byte.TryParse(f[0], out var o) && byte.TryParse(f[1], out var h) && byte.TryParse(f[2], out var hc) && byte.TryParse(f[3], out var ac) && byte.TryParse(f[4], out var c))
                a = new Appearance { Outfit = o, Hair = h, HairColor = hc, Accessory = ac, Color = c, Weapon = f.Length >= 6 && byte.TryParse(f[5], out var wp) ? wp : (byte)0 };
            return a;
        }
    }
}
