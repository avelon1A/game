using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteNetLib;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Veil.Sim;

namespace Veil.Server
{
    /// <summary>One seat in an assigned match (a human from a party).</summary>
    public sealed class Seat
    {
        public string ProfileId = "";
        public string Name = "Player";
        public Appearance Look;
        public int Squad;
    }

    /// <summary>Created by the matchmaker; the host builds the match on its own thread.</summary>
    public sealed class MatchRequest
    {
        public int MatchId;
        public int Seconds = 900;
        public readonly List<Seat> Seats = new List<Seat>();
    }

    /// <summary>
    /// Authoritative UDP match host (LiteNetLib). Runs any number of <see cref="MatchInstance"/>s on one thread.
    /// Players join with a signed ticket from the Gateway; the same ticket reconnects them to their seat.
    /// Later this class runs on dedicated machines (allocator/fleet) — it only needs the ticket secret.
    /// </summary>
    public sealed class GameHost : BackgroundService
    {
        public const string ConnectionKey = "VEIL";

        private readonly ServerOptions _opt;
        private readonly Database _db;
        private readonly TicketSigner _tickets;
        private readonly ILogger<GameHost> _log;
        private readonly ConcurrentQueue<MatchRequest> _pending = new ConcurrentQueue<MatchRequest>();
        private readonly Dictionary<int, MatchInstance> _matches = new Dictionary<int, MatchInstance>();
        private readonly Dictionary<int, MatchInstance> _byPeer = new Dictionary<int, MatchInstance>();
        private readonly ByteWriter _w = new ByteWriter(512);
        private NetManager _net;
        private MapData _map;

        /// <summary>Raised on the host thread when a match finishes: (matchId, results, profile ids of humans).</summary>
        public event Action<int, List<PlayerResult>, Dictionary<int, string>> MatchEnded;

        // ---- read by other threads ----
        public volatile int PublicMatches;
        public volatile int PublicPlayers;
        public long MatchesPlayed;
        public double LastTickMs;

        public GameHost(ServerOptions opt, Database db, TicketSigner tickets, ILogger<GameHost> log)
        {
            _opt = opt; _db = db; _tickets = tickets; _log = log;
        }

        public ServerOptions Options => _opt;
        public void Enqueue(MatchRequest r) => _pending.Enqueue(r);

        protected override Task ExecuteAsync(CancellationToken stop) =>
            Task.Factory.StartNew(() => Loop(stop), stop, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        private void Loop(CancellationToken stop)
        {
            var sw = Stopwatch.StartNew();
            _map = ArenaMap.Build();
            _log.LogInformation("Arena built in {Ms} ms ({Obstacles} obstacles)", sw.ElapsedMilliseconds, _map.Obstacles.Count);

            var listener = new EventBasedNetListener();
            _net = new NetManager(listener)
            {
                AutoRecycle = true, ChannelsCount = 2, DisconnectTimeout = 8000, UpdateTime = 10,
                UnconnectedMessagesEnabled = true,   // LAN discovery replies
                BroadcastReceiveEnabled = true,      // LAN discovery requests from phones/PCs
            };
            // LAN discovery: clients broadcast "VEIL?"; answer "VEIL!|<name>|<udp port>|<players>" to the sender
            listener.NetworkReceiveUnconnectedEvent += (ep, reader, type) =>
            {
                if (System.Text.Encoding.UTF8.GetString(reader.GetRemainingBytes()) != "VEIL?") return;
                _net.SendUnconnectedMessage(System.Text.Encoding.UTF8.GetBytes($"VEIL!|{_opt.Name}|{_opt.UdpPort}|{PublicPlayers}"), ep);
            };
            listener.ConnectionRequestEvent += req => req.AcceptIfKey(ConnectionKey);
            listener.PeerConnectedEvent += peer => _log.LogInformation("Peer {Id} connected from {Addr}", peer.Id, peer);
            listener.PeerDisconnectedEvent += (peer, info) =>
            {
                if (_byPeer.Remove(peer.Id, out var m)) m.Unseat(peer, info.Reason.ToString());
            };
            listener.NetworkReceiveEvent += (peer, reader, channel, method) =>
            {
                var data = reader.GetRemainingBytes();
                try { OnPacket(peer, data); }
                catch (Exception e) { _log.LogWarning("Bad packet from {Id}: {Err}", peer.Id, e.Message); }
            };

            bool bound = false;
            for (int i = 0; i < 20 && !bound && !stop.IsCancellationRequested; i++) { bound = _net.Start(_opt.UdpPort); if (!bound) Thread.Sleep(500); }
            if (!bound) { _log.LogError("Could not bind UDP port {Port}", _opt.UdpPort); return; }
            _log.LogInformation("Match host listening on UDP {Port}", _opt.UdpPort);

            double acc = 0, last = sw.Elapsed.TotalSeconds;
            var tickSw = new Stopwatch();
            var done = new List<int>();
            while (!stop.IsCancellationRequested)
            {
                _net.PollEvents();
                while (_pending.TryDequeue(out var req))
                {
                    var inst = new MatchInstance(req, _map, _db, _log);
                    inst.Ended += (id, results, profiles) => { Interlocked.Increment(ref MatchesPlayed); MatchEnded?.Invoke(id, results, profiles); };
                    _matches[req.MatchId] = inst;
                    _log.LogInformation("Match {Id} created: {Humans} humans, {Secs}s", req.MatchId, req.Seats.Count, req.Seconds);
                }
                double now = sw.Elapsed.TotalSeconds;
                acc += now - last; last = now;
                if (acc > 0.25) acc = 0.25;
                while (acc >= GameConfig.Dt)
                {
                    acc -= GameConfig.Dt;
                    tickSw.Restart();
                    done.Clear();
                    foreach (var m in _matches.Values)
                    {
                        m.Tick(GameConfig.Dt);
                        if (m.Disposable) done.Add(m.Id);
                    }
                    foreach (int id in done)
                    {
                        var m = _matches[id];
                        foreach (var peer in m.Peers.ToList()) { _byPeer.Remove(peer.Id); peer.Disconnect(); }
                        _matches.Remove(id);
                        _log.LogInformation("Match {Id} closed", id);
                    }
                    LastTickMs = tickSw.Elapsed.TotalMilliseconds;
                    PublicMatches = _matches.Count;
                    PublicPlayers = _byPeer.Count;
                }
                Thread.Sleep(1);
            }
            _net.Stop();
        }

        private void OnPacket(NetPeer peer, byte[] data)
        {
            if (data.Length == 0) return;
            var r = new ByteReader(data, 1, data.Length - 1);
            var msg = (Msg)data[0];
            if (msg == Msg.Hello)
            {
                var h = Protocol.ReadHello(r);
                if (h.Protocol != GameConfig.ProtocolVersion) { Reject(peer, $"Version mismatch (server {GameConfig.ProtocolVersion}, client {h.Protocol}) — update the game"); return; }
                var t = _tickets.Validate(h.Ticket, out string err);
                if (t == null) { Reject(peer, "Join through a party: " + err); return; }
                if (!_matches.TryGetValue(t.MatchId, out var m) || m.Finished) { Reject(peer, "That match has ended"); return; }
                if (_byPeer.Remove(peer.Id, out var old)) old.Unseat(peer, "rehello");
                var displaced = m.Seat(peer, t, h.Look);
                if (displaced == null) { Reject(peer, "No seat for you in this match"); return; }
                if (displaced != peer) { _byPeer.Remove(displaced.Id); displaced.Disconnect(); }
                _byPeer[peer.Id] = m;
                return;
            }
            if (_byPeer.TryGetValue(peer.Id, out var match)) match.OnPacket(peer, msg, r);
        }

        private void Reject(NetPeer peer, string reason)
        {
            _w.Reset(); Protocol.WriteReject(_w, reason);
            peer.Send(_w.Buffer, 0, _w.Length, 1, DeliveryMethod.ReliableOrdered);
            _log.LogInformation("Rejected peer {Id}: {Reason}", peer.Id, reason);
            peer.Disconnect();
        }

        public object Describe() => new
        {
            name = _opt.Name,
            port = _opt.UdpPort,
            matches = PublicMatches,
            players = PublicPlayers,
            matchesPlayed = Interlocked.Read(ref MatchesPlayed),
            lastTickMs = Math.Round(LastTickMs, 3),
        };
    }

    /// <summary>One running 4×4 match. Only touched by the host thread.</summary>
    public sealed class MatchInstance
    {
        private sealed class Human
        {
            public Seat Seat;
            public int PlayerId;
            public NetPeer Peer;
            public bool EverConnected;
        }

        public readonly int Id;
        private readonly MatchRequest _req;
        private readonly Database _db;
        private readonly ILogger _log;
        private readonly MatchSim _sim;
        private readonly List<Human> _humans = new List<Human>();
        private readonly ByteWriter _w = new ByteWriter(4096);
        private readonly Snapshot _snap = new Snapshot();
        private readonly List<InputCmd> _inputs = new List<InputCmd>();
        private readonly List<SimEvent> _events = new List<SimEvent>();
        private float _waitT = 12f;       // max wait for everyone to connect before starting
        private float _lingerT = -1;       // after the end: keep results available, then dispose
        private bool _recorded;

        public event Action<int, List<PlayerResult>, Dictionary<int, string>> Ended;

        public bool Finished => _sim.Ended;
        public bool Disposable => _lingerT == 0;
        public IEnumerable<NetPeer> Peers => _humans.Where(h => h.Peer != null).Select(h => h.Peer);

        public MatchInstance(MatchRequest req, MapData map, Database db, ILogger log)
        {
            Id = req.MatchId; _req = req; _db = db; _log = log;
            _sim = new MatchSim(map, new MatchSettings { MatchSeconds = req.Seconds, TotalPlayers = GameConfig.MaxPlayers, Seed = Environment.TickCount & 0x7fffffff });
            foreach (var s in req.Seats)
            {
                // seated humans are bot-piloted until they connect (and whenever they drop)
                var p = _sim.AddPlayer(s.Name, s.Look, false, BotKind.None, s.Squad);
                _humans.Add(new Human { Seat = s, PlayerId = p.Id });
            }
            _sim.FillBots();
            foreach (var h in _humans) Pilot(h, true);
        }

        private void Pilot(Human h, bool on)
        {
            var p = _sim.Players[h.PlayerId];
            _sim.Bots.RemoveAll(b => b.Player == p);
            p.Connected = !on;
            p.BotKind = on ? BotKind.Explorer : BotKind.None;
            if (on) _sim.Bots.Add(new BotBrain(_sim, p, Environment.TickCount + h.PlayerId));
            p.PendingInputs.Clear();
        }

        /// <summary>Binds a peer to its seat. Returns the peer that now owns the seat's old connection (to drop), the
        /// same peer when the seat was free, or null if this ticket has no seat here.</summary>
        public NetPeer Seat(NetPeer peer, TicketData t, Appearance look)
        {
            var h = _humans.FirstOrDefault(x => x.Seat.ProfileId == t.ProfileId);
            if (h == null) return null;
            var old = h.Peer;
            h.Peer = peer;
            bool rejoin = h.EverConnected;
            h.EverConnected = true;
            Pilot(h, false);
            _w.Reset(); Protocol.WriteWelcome(_w, h.PlayerId, $"Match {Id}");
            Send(peer, DeliveryMethod.ReliableOrdered);
            if (_sim.Started) SendStart(h);
            _log.LogInformation("{Name} {What} match {Id} (squad {Squad})", h.Seat.Name, rejoin ? "rejoined" : "joined", Id, (char)('A' + h.Seat.Squad));
            return old ?? peer;
        }

        public void Unseat(NetPeer peer, string reason)
        {
            var h = _humans.FirstOrDefault(x => x.Peer == peer);
            if (h == null) return;
            h.Peer = null;
            if (!_sim.Ended) Pilot(h, true);   // a bot keeps the character alive until they reconnect
            _log.LogInformation("{Name} dropped from match {Id} ({Reason}) — bot piloting", h.Seat.Name, Id, reason);
        }

        public void OnPacket(NetPeer peer, Msg msg, ByteReader r)
        {
            var h = _humans.FirstOrDefault(x => x.Peer == peer);
            if (h == null) return;
            switch (msg)
            {
                case Msg.Input:
                    if (!_sim.Started) return;
                    Protocol.ReadInputs(r, _inputs);
                    foreach (var cmd in _inputs) _sim.SubmitInput(h.PlayerId, cmd);
                    break;
                case Msg.Leave:
                    peer.Disconnect();
                    break;
            }
        }

        public void Tick(float dt)
        {
            if (_lingerT > 0) { _lingerT = Math.Max(0, _lingerT - dt); return; }
            if (!_sim.Started)
            {
                _waitT -= dt;
                if (_waitT <= 0 || _humans.All(h => h.Peer != null)) Start();
                return;
            }
            _sim.Step();
            SendEvents();
            if (_sim.Tick % GameConfig.SnapshotEveryTicks == 0 || _sim.Ended)
            {
                foreach (var h in _humans)
                {
                    if (h.Peer == null) continue;
                    SnapshotBuilder.Build(_sim, _sim.Players[h.PlayerId], _snap);
                    if (_snap.Projectiles.Count > 40) _snap.Projectiles.RemoveRange(40, _snap.Projectiles.Count - 40);
                    _w.Reset(); Protocol.WriteSnapshot(_w, _snap);
                    Send(h.Peer, DeliveryMethod.Sequenced, 0);
                }
            }
            if (_sim.Ended && !_recorded) Finish();
        }

        private void Start()
        {
            _sim.Start();
            foreach (var h in _humans) if (h.Peer != null) SendStart(h);
            SendEvents();
            _log.LogInformation("Match {Id} started: {Humans} humans ({Connected} connected) + {Bots} bots",
                Id, _humans.Count, _humans.Count(h => h.Peer != null), _sim.Players.Count - _humans.Count);
        }

        private void SendStart(Human h)
        {
            var ms = new MatchStartMsg { Seed = _sim.Settings.Seed, MatchSeconds = _sim.Settings.MatchSeconds, YourPlayerId = h.PlayerId };
            int mySquad = _sim.Players[h.PlayerId].Squad;
            foreach (var p in _sim.Players)
            {
                var seat = _humans.FirstOrDefault(x => x.PlayerId == p.Id);
                ms.Roster.Add(new RosterEntry
                {
                    Id = p.Id, Name = p.Name, Look = p.Look, IsBot = p.IsBot, Squad = p.Squad,
                    ProfileId = seat != null && p.Squad == mySquad ? seat.Seat.ProfileId : "",
                });
            }
            _w.Reset(); Protocol.WriteMatchStart(_w, ms);
            Send(h.Peer, DeliveryMethod.ReliableOrdered);
        }

        private void Finish()
        {
            _recorded = true;
            _w.Reset(); Protocol.WriteMatchEnd(_w, _sim.Results);
            foreach (var h in _humans) if (h.Peer != null) Send(h.Peer, DeliveryMethod.ReliableOrdered);
            var profiles = _humans.ToDictionary(h => h.PlayerId, h => h.Seat.ProfileId);
            try
            {
                long id = _db.RecordMatch((int)_sim.Duration, _sim.Results, profiles);
                var top = _sim.Squads.OrderBy(s => s.Rank).First();
                _log.LogInformation("Match {Id} ended (db {Db}). Winning squad {Squad} with {Score}", Id, id, (char)('A' + top.Id), top.Total);
            }
            catch (Exception e) { _log.LogError("Failed to record match: {Err}", e.Message); }
            Ended?.Invoke(Id, _sim.Results, profiles);
            _lingerT = 10f;
        }

        private void SendEvents()
        {
            if (_sim.Events.Count == 0) return;
            foreach (var h in _humans)
            {
                if (h.Peer == null) continue;
                var viewer = _sim.Players[h.PlayerId];
                _events.Clear();
                foreach (var e in _sim.Events)
                {
                    var ev = e;
                    if (SnapshotBuilder.FilterEvent(_sim, viewer, ref ev)) _events.Add(ev);
                }
                if (_events.Count == 0) continue;
                _w.Reset(); Protocol.WriteEvents(_w, _events);
                Send(h.Peer, DeliveryMethod.ReliableOrdered);
            }
        }

        private void Send(NetPeer peer, DeliveryMethod method, byte channel = 1)
        {
            if ((method == DeliveryMethod.Unreliable || method == DeliveryMethod.Sequenced) && _w.Length > peer.GetMaxSinglePacketSize(method))
                method = DeliveryMethod.ReliableUnordered;
            peer.Send(_w.Buffer, 0, _w.Length, channel, method);
        }
    }
}
