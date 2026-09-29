using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LiteNetLib;
using Veil.Sim;

namespace Veil.LoadTest
{
    /// <summary>
    /// End-to-end squad test against a running Veil.Server:
    /// REST register → Gateway hello → friend requests → party create / invite / join-by-code / kick / promote →
    /// ready → matchmaking (party kept together as a squad, second party in another squad) → UDP join with ticket →
    /// squad-aware snapshots → voice relay (squad-only forwarding) → drop + rejoin with the same ticket → match end →
    /// parties back to idle. Usage: dotnet run -- [host=127.0.0.1] [http=5080] [--quick]
    /// </summary>
    public static class Program
    {
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true, PropertyNameCaseInsensitive = true };
        private static string _host = "127.0.0.1";
        private static int _http = 5080;
        private static int _failures;

        private sealed class RegisterReply { public string id { get; set; } public string token { get; set; } }

        private sealed class Client
        {
            public string Name = "", Id = "", Token = "", Handle = "";
            public ClientWebSocket Ws;
            public readonly ConcurrentQueue<GwEnvelope> Inbox = new ConcurrentQueue<GwEnvelope>();
            public readonly List<GwEnvelope> Seen = new List<GwEnvelope>();
            public PartyInfo Party = new PartyInfo();
            public FriendsState Friends = new FriendsState();
            public MatchAssignedMsg Assigned;
            public int Req;
            // match
            public NetManager Net;
            public NetPeer Peer;
            public int PlayerId = -1, Squad = -1, Snapshots, AllyFlagged, MatchStarts;
            public bool Ended;
            public List<PlayerResult> Results;
            public int Seq;
            public readonly List<InputCmd> Recent = new List<InputCmd>();
            public readonly ByteWriter W = new ByteWriter(256);
            public string Rejected = "";
        }

        public static async Task<int> Main(string[] args)
        {
            if (args.Length > 0 && !args[0].StartsWith("--")) _host = args[0];
            if (args.Length > 1 && !args[1].StartsWith("--")) _http = int.Parse(args[1]);
            Console.WriteLine($"VEIL squad flow test → http://{_host}:{_http}");
            var http = new HttpClient { BaseAddress = new Uri($"http://{_host}:{_http}") };

            // ---------------- accounts + gateway ----------------
            var names = new[] { "Alpha", "Bravo", "Charlie", "Delta", "Echo", "Foxtrot" };
            var cs = new List<Client>();
            foreach (var n in names)
            {
                var res = await http.PostAsJsonAsync("/api/players/register", new { name = n + Random.Shared.Next(100, 999) });
                var reg = await res.Content.ReadFromJsonAsync<RegisterReply>();
                var c = new Client { Name = n, Id = reg.id, Token = reg.token };
                await ConnectGateway(c);
                cs.Add(c);
            }
            foreach (var c in cs)
            {
                var w = await WaitFor<GwWelcome>(c, Gw.Welcome);
                c.Handle = w.handle;
            }
            Check(cs.All(c => c.Handle.Contains('#')), "every player has a Name#1234 handle");
            var (a, b, ch, d, e, f) = (cs[0], cs[1], cs[2], cs[3], cs[4], cs[5]);

            // ---------------- friends ----------------
            foreach (var x in new[] { b, ch, d }) Check(await Request(a, Gw.FriendRequest, new GwText { text = x.Handle }), $"{a.Name} → friend request {x.Name}");
            Check(!await Request(a, Gw.FriendRequest, new GwText { text = "Nobody#0001" }), "unknown handle is refused");
            foreach (var x in new[] { b, ch, d })
            {
                await Until(x, () => x.Friends.incoming.Any(i => i.id == a.Id), "incoming request");
                long rid = x.Friends.incoming.First(i => i.id == a.Id).requestId;
                Check(await Request(x, Gw.FriendAccept, new GwRequestId { requestId = rid }), $"{x.Name} accepts");
            }
            await Until(a, () => a.Friends.friends.Count == 3, "3 friends");
            Check(a.Friends.friends.All(fr => fr.status >= (int)PresenceStatus.Online), "friends show as online");
            Check(!await Request(e, Gw.PartyInvite, new GwId { id = a.Id }), "can't invite a non-friend");

            // ---------------- party ----------------
            Check(await Request(a, Gw.PartyCreate, null), "create room");
            await Until(a, () => !a.Party.Empty, "party state");
            string code = a.Party.code;
            Console.WriteLine($"  room code {code}");
            foreach (var x in new[] { b, ch })
            {
                Check(await Request(a, Gw.PartyInvite, new GwId { id = x.Id }), $"invite {x.Name}");
                var inv = await WaitFor<IncomingInvite>(x, Gw.Invite);
                Check(await Request(x, Gw.InviteAccept, new GwId { id = inv.inviteId }), $"{x.Name} accepts invite");
            }
            Check(await Request(d, Gw.PartyJoin, new GwText { text = code.ToLowerInvariant() }), "join by room code");
            await Until(a, () => a.Party.members.Count == 4, "4 members");
            Check(!await Request(e, Gw.PartyJoin, new GwText { text = code }), "5th player can't join a full party");
            Check(!await Request(b, Gw.PartyKick, new GwId { id = d.Id }), "non-leader can't kick");
            Check(await Request(a, Gw.PartyKick, new GwId { id = d.Id }), "leader kicks");
            await Until(d, () => d.Party.Empty, "kicked player has no party");
            Check(await Request(d, Gw.PartyJoin, new GwText { text = code }), "kicked player rejoins by code");
            Check(await Request(a, Gw.PartyPromote, new GwId { id = b.Id }), "transfer leadership");
            await Until(a, () => a.Party.leader == b.Id, "b is leader");
            Check(await Request(b, Gw.PartyPromote, new GwId { id = a.Id }), "transfer back");
            await Until(b, () => b.Party.leader == a.Id, "a is leader again");
            await Until(b, () => b.Friends.friends.Any(fr => fr.id == a.Id && fr.status == (int)PresenceStatus.InParty), "presence: In party");

            Check(await Request(e, Gw.PartyCreate, null), "second party");
            await Until(e, () => !e.Party.Empty, "second party state");
            Check(await Request(f, Gw.PartyJoin, new GwText { text = e.Party.code }), "join second party");

            // ---------------- ready + queue ----------------
            Check(!await Request(a, Gw.PartyStart, new GwStart { seconds = 60 }), "can't start before everyone is ready");
            foreach (var x in new[] { b, ch, d, f }) Check(await Request(x, Gw.PartyReady, new GwFlag { value = true }), $"{x.Name} ready");
            Check(await Request(a, Gw.PartyStart, new GwStart { seconds = 60 }), "leader starts (queue)");
            Check(await Request(e, Gw.PartyStart, new GwStart { seconds = 60 }), "second leader starts");
            foreach (var c in cs) c.Assigned = await WaitFor<MatchAssignedMsg>(c, Gw.MatchAssigned, 20);
            var sq1 = cs.Take(4).Select(c => c.Assigned.squad).Distinct().ToList();
            var sq2 = cs.Skip(4).Select(c => c.Assigned.squad).Distinct().ToList();
            Check(cs.Select(c => c.Assigned.matchId).Distinct().Count() == 1, "both parties in the same match");
            Check(sq1.Count == 1 && sq2.Count == 1 && sq1[0] != sq2[0], $"parties kept together as squads ({(char)('A' + sq1[0])} / {(char)('A' + sq2[0])})");

            // ---------------- UDP match ----------------
            foreach (var c in cs) StartUdp(c);
            await PumpUntil(cs, () => cs.All(c => c.MatchStarts > 0 && c.Snapshots > 5), 20, "all clients receive the match");
            Check(cs.All(c => c.PlayerId >= 0), "every client seated");
            await Until(b, () => b.Friends.friends.Any(fr => fr.id == a.Id && fr.status == (int)PresenceStatus.InMatch), "presence: In match");

            // voice: squad-only forwarding
            await VoiceTest(a, b, e);

            // reconnect with the same ticket
            ch.Peer.Disconnect();
            await PumpUntil(cs, () => false, 1.5, null);
            int startsBefore = ch.MatchStarts, snapsBefore = ch.Snapshots;
            StartUdp(ch);
            await PumpUntil(cs, () => ch.MatchStarts > startsBefore && ch.Snapshots > snapsBefore + 5, 15, "rejoin with the same ticket");
            Check(cs.Take(4).Where(c => c != ch).All(c => c.AllyFlagged > 0), "squadmates flagged as allies in snapshots");

            // app restart mid-match → Gateway offers REJOIN
            await d.Ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "restart", CancellationToken.None);
            await ConnectGateway(d);
            var again = await WaitFor<MatchAssignedMsg>(d, Gw.MatchAssigned, 10);
            Check(again.rejoin && again.matchId == a.Assigned.matchId, "gateway offers rejoin after app restart");

            // play to the end (60 s match)
            Console.WriteLine("  playing the 60 s match…");
            await PumpUntil(cs, () => cs.All(c => c.Ended), 120, "match ends");
            var results = a.Results ?? new List<PlayerResult>();
            Check(results.Count == GameConfig.MaxPlayers, $"16 results ({results.Count})");
            Check(results.Select(r => r.SquadRank).Distinct().Count() == GameConfig.SquadCount, "4 ranked squads");
            foreach (var c in cs) await WaitFor<GwId>(c, Gw.MatchFinished, 15);
            await Until(a, () => a.Party.phase == (int)PartyPhase.Idle && a.Party.members.All(m => !m.ready || m.leader), "party back to idle");
            var prof = await http.GetFromJsonAsync<JsonElement>($"/api/players/{a.Id}");
            Check(prof.GetProperty("matches").GetInt32() == 1, "match recorded on the profile");

            foreach (var c in cs) { c.Net?.Stop(); try { await c.Ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); } catch { } }
            Console.WriteLine(_failures == 0 ? "\nSQUAD FLOW PASS" : $"\nSQUAD FLOW FAIL ({_failures})");
            return _failures == 0 ? 0 : 1;
        }

        // ================================================================== gateway

        private static async Task ConnectGateway(Client c)
        {
            c.Ws = new ClientWebSocket();
            await c.Ws.ConnectAsync(new Uri($"ws://{_host}:{_http}/ws"), CancellationToken.None);
            _ = Task.Run(() => Receive(c, c.Ws));
            var ws = c.Ws;
            _ = Task.Run(async () =>   // heartbeat like the game client (the Gateway drops silent sockets after 45 s)
            {
                while (ws.State == WebSocketState.Open)
                {
                    await Task.Delay(10000);
                    try { await SendRaw(c, new GwEnvelope { t = Gw.Ping, d = JsonSerializer.Serialize(new GwPing { clientMs = Environment.TickCount64, rttMs = 5 }, Json) }); } catch { }
                }
            });
            await SendRaw(c, new GwEnvelope { t = Gw.Hello, d = JsonSerializer.Serialize(new GwHello { id = c.Id, token = c.Token, name = "", look = "" }, Json) });
        }

        private static async Task Receive(Client c, ClientWebSocket ws)
        {
            var buf = new byte[64 * 1024];
            var sb = new StringBuilder();
            try
            {
                while (ws.State == WebSocketState.Open)
                {
                    sb.Clear();
                    WebSocketReceiveResult r;
                    do { r = await ws.ReceiveAsync(buf, CancellationToken.None); sb.Append(Encoding.UTF8.GetString(buf, 0, r.Count)); } while (!r.EndOfMessage);
                    if (r.MessageType == WebSocketMessageType.Close) break;
                    var env = JsonSerializer.Deserialize<GwEnvelope>(sb.ToString(), Json);
                    if (env.t == Gw.PartyState) c.Party = JsonSerializer.Deserialize<PartyInfo>(env.d, Json);
                    else if (env.t == Gw.FriendsState) c.Friends = JsonSerializer.Deserialize<FriendsState>(env.d, Json);
                    else if (env.t == Gw.Presence)
                    {
                        var p = JsonSerializer.Deserialize<FriendEntry>(env.d, Json);
                        var fr = c.Friends.friends.FirstOrDefault(x => x.id == p.id);
                        if (fr != null) { fr.status = p.status; fr.detail = p.detail; }
                    }
                    c.Inbox.Enqueue(env);
                }
            }
            catch { }
        }

        private static async Task SendRaw(Client c, GwEnvelope env) =>
            await c.Ws.SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(env, Json)), WebSocketMessageType.Text, true, CancellationToken.None);

        /// <summary>Sends a request and waits for its reply. Returns ok.</summary>
        private static async Task<bool> Request(Client c, string type, object payload)
        {
            string id = $"r{++c.Req}";
            await SendRaw(c, new GwEnvelope { t = type, id = id, d = payload == null ? "" : JsonSerializer.Serialize(payload, payload.GetType(), Json) });
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                Drain(c);
                var rep = c.Seen.FirstOrDefault(x => x.t == Gw.Reply && x.id == id);
                if (rep != null) { if (!rep.ok) Console.WriteLine($"      ({c.Name}: {rep.err})"); return rep.ok; }
                await Task.Delay(10);
            }
            Console.WriteLine($"      ({c.Name}: no reply to {type})");
            return false;
        }

        private static void Drain(Client c) { while (c.Inbox.TryDequeue(out var e)) c.Seen.Add(e); }

        private static async Task<T> WaitFor<T>(Client c, string type, double seconds = 5) where T : new()
        {
            var deadline = DateTime.UtcNow.AddSeconds(seconds);
            while (DateTime.UtcNow < deadline)
            {
                Drain(c);
                int i = c.Seen.FindIndex(x => x.t == type);
                if (i >= 0) { var env = c.Seen[i]; c.Seen.RemoveAt(i); return JsonSerializer.Deserialize<T>(env.d, Json); }
                PumpUdp(c);
                await Task.Delay(10);
            }
            Check(false, $"{c.Name} waiting for {type}");
            return new T();
        }

        private static async Task Until(Client c, Func<bool> cond, string what, double seconds = 5)
        {
            var deadline = DateTime.UtcNow.AddSeconds(seconds);
            while (DateTime.UtcNow < deadline) { Drain(c); if (cond()) return; await Task.Delay(10); }
            Check(false, $"{c.Name}: {what}");
        }

        // ================================================================== match (UDP)

        private static void StartUdp(Client c)
        {
            c.Net?.Stop();
            var l = new EventBasedNetListener();
            c.Net = new NetManager(l) { AutoRecycle = true, ChannelsCount = 2, UpdateTime = 10 };
            l.PeerConnectedEvent += p =>
            {
                c.Peer = p;
                c.W.Reset();
                Protocol.WriteHello(c.W, new HelloMsg { Name = c.Name, Look = Appearance.Preset(c.Name.Length), ProfileId = c.Id, Ticket = c.Assigned.ticket });
                p.Send(c.W.Buffer, 0, c.W.Length, 1, DeliveryMethod.ReliableOrdered);
            };
            l.NetworkReceiveEvent += (p, r, chn, m) =>
            {
                var data = r.GetRemainingBytes();
                var br = new ByteReader(data, 1, data.Length - 1);
                switch ((Msg)data[0])
                {
                    case Msg.Welcome: c.PlayerId = br.I32(); break;
                    case Msg.Reject: c.Rejected = br.Str(); Console.WriteLine($"      ({c.Name} rejected: {c.Rejected})"); break;
                    case Msg.MatchStart:
                    {
                        var ms = Protocol.ReadMatchStart(br);
                        c.PlayerId = ms.YourPlayerId;
                        c.Squad = ms.Roster.First(x => x.Id == ms.YourPlayerId).Squad;
                        c.MatchStarts++;
                        break;
                    }
                    case Msg.Snapshot:
                    {
                        var s = Protocol.ReadSnapshot(br);
                        c.Snapshots++;
                        if (s.Avatars.Any(av => (av.Flags & AvatarFlags.Ally) != 0)) c.AllyFlagged++;
                        break;
                    }
                    case Msg.MatchEnd: c.Results = Protocol.ReadMatchEnd(br); c.Ended = true; break;
                }
            };
            c.Net.Start();
            c.Net.Connect(_host, c.Assigned.port, GameHostKey);
        }

        private const string GameHostKey = "VEIL";

        private static void PumpUdp(Client c)
        {
            if (c.Net == null) return;
            c.Net.PollEvents();
            if (c.Peer == null || c.Peer.ConnectionState != ConnectionState.Connected || c.MatchStarts == 0) return;
            var cmd = new InputCmd { Seq = ++c.Seq, MoveX = MathF.Sin(c.Seq * 0.05f), MoveY = MathF.Cos(c.Seq * 0.031f), Yaw = c.Seq % 360 };
            c.Recent.Add(Protocol.Quantize(cmd));
            if (c.Recent.Count > 4) c.Recent.RemoveAt(0);
            c.W.Reset(); Protocol.WriteInputs(c.W, c.Recent, 0, c.Recent.Count);
            c.Peer.Send(c.W.Buffer, 0, c.W.Length, 0, DeliveryMethod.Unreliable);
        }

        private static async Task PumpUntil(List<Client> cs, Func<bool> cond, double seconds, string what)
        {
            var deadline = DateTime.UtcNow.AddSeconds(seconds);
            while (DateTime.UtcNow < deadline)
            {
                foreach (var c in cs) { PumpUdp(c); Drain(c); }
                if (cond()) return;
                await Task.Delay(33);
            }
            if (what != null) Check(false, what);
        }

        // ================================================================== voice

        private static async Task VoiceTest(Client speaker, Client mate, Client enemy)
        {
            int port = 7778;
            using var us = new UdpClient(0); using var um = new UdpClient(0); using var ue = new UdpClient(0);
            var server = new IPEndPoint(IPAddress.Parse(_host == "localhost" ? "127.0.0.1" : _host), port);
            var w = new ByteWriter(700);
            void Join(UdpClient u, Client c) { VoiceWire.WriteJoin(w, c.Assigned.voiceChannel, c.Id, c.Assigned.voiceToken); u.Send(w.Buffer, w.Length, server); }
            Join(us, speaker); Join(um, mate); Join(ue, enemy);
            // forged token must be refused
            VoiceWire.WriteJoin(w, speaker.Assigned.voiceChannel, enemy.Id, "forged");
            using var uf = new UdpClient(0);
            uf.Send(w.Buffer, w.Length, server);
            await Task.Delay(300);
            for (ushort i = 0; i < 20; i++)
            {
                w.Reset(); w.U8(VoiceWire.Frame); w.U16(i); for (int k = 0; k < 40; k++) w.U8((byte)k);
                us.Send(w.Buffer, w.Length, server);
                await Task.Delay(20);
            }
            await Task.Delay(300);
            int mateFrames = Count(um, VoiceWire.FrameOut), enemyFrames = Count(ue, VoiceWire.FrameOut), forged = Count(uf, VoiceWire.Denied);
            Check(mateFrames >= 18, $"voice: squadmate hears the speaker ({mateFrames}/20 frames)");
            Check(enemyFrames == 0, $"voice: other squad hears nothing ({enemyFrames})");
            Check(forged == 1, "voice: forged token denied");
        }

        private static int Count(UdpClient u, byte type)
        {
            int n = 0;
            while (u.Available > 0)
            {
                IPEndPoint any = null;
                var d = u.Receive(ref any);
                if (d.Length > 0 && d[0] == type) n++;
            }
            return n;
        }

        private static void Check(bool ok, string what)
        {
            Console.WriteLine($"  {(ok ? "✓" : "✗")} {what}");
            if (!ok) _failures++;
        }
    }
}
