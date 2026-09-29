using System;
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
    /// <summary>
    /// Authoritative UDP game server. One lobby → one match at a time (enough for a test server).
    /// All game logic runs on a single thread: network polling, simulation and sending.
    /// </summary>
    public sealed class GameServer : BackgroundService
    {
        public const string ConnectionKey = "VEIL";

        private sealed class Client
        {
            public NetPeer Peer;
            public int ClientId;
            public string Name = "Player";
            public Appearance Look;
            public string ProfileId = "";
            public bool Ready;
            public bool Welcomed;
            public int PlayerId = -1;
            public int PreferredSeconds;
        }

        private readonly ServerOptions _opt;
        private readonly Database _db;
        private readonly ILogger<GameServer> _log;
        private readonly Dictionary<int, Client> _clients = new Dictionary<int, Client>();
        private readonly ByteWriter _w = new ByteWriter(4096);
        private readonly List<InputCmd> _inputScratch = new List<InputCmd>();
        private readonly List<SimEvent> _eventScratch = new List<SimEvent>();
        private readonly Snapshot _snap = new Snapshot();
        private NetManager _net;
        private MapData _map;
        private MatchSim _sim;
        private LobbyStatus _status = LobbyStatus.Waiting;
        private float _countdown;
        private float _resultsTimer;
        private float _lobbyBroadcastT;
        private int _hostClientId = -1;
        private readonly Dictionary<int, string> _profileByPlayer = new Dictionary<int, string>();

        // ---- read by the REST API thread ----
        public volatile int PublicHumans;
        public volatile int PublicStatus;
        public volatile int PublicMatchSecondsLeft;
        public long MatchesPlayed;
        public double LastTickMs;

        public GameServer(ServerOptions opt, Database db, ILogger<GameServer> log)
        {
            _opt = opt;
            _db = db;
            _log = log;
        }

        public ServerOptions Options => _opt;
        public string StatusName => ((LobbyStatus)PublicStatus).ToString();

        protected override Task ExecuteAsync(CancellationToken stop)
        {
            return Task.Factory.StartNew(() => Loop(stop), stop, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        private void Loop(CancellationToken stop)
        {
            var sw = Stopwatch.StartNew();
            _map = ArenaMap.Build();
            _log.LogInformation("Arena built in {Ms} ms ({Obstacles} obstacles)", sw.ElapsedMilliseconds, _map.Obstacles.Count);

            var listener = new EventBasedNetListener();
            _net = new NetManager(listener)
            {
                AutoRecycle = true,
                ChannelsCount = 2,
                DisconnectTimeout = 8000,
                UpdateTime = 10,
                UnconnectedMessagesEnabled = true,   // LAN discovery replies
                BroadcastReceiveEnabled = true,      // LAN discovery requests from phones/PCs
            };
            // LAN discovery: clients broadcast "VEIL?"; answer "VEIL!|<name>|<udp port>" to the sender
            listener.NetworkReceiveUnconnectedEvent += (ep, reader, type) =>
            {
                var msg = System.Text.Encoding.UTF8.GetString(reader.GetRemainingBytes());
                if (msg != "VEIL?") return;
                var reply = System.Text.Encoding.UTF8.GetBytes($"VEIL!|{_opt.Name}|{_opt.UdpPort}|{_clients.Count}");
                _net.SendUnconnectedMessage(reply, ep);
            };
            listener.ConnectionRequestEvent += req =>
            {
                if (_clients.Count >= GameConfig.MaxPlayers) req.Reject();
                else req.AcceptIfKey(ConnectionKey);
            };
            listener.PeerConnectedEvent += peer =>
            {
                _clients[peer.Id] = new Client { Peer = peer, ClientId = peer.Id };
                _log.LogInformation("Peer {Id} connected from {Addr}", peer.Id, peer);
            };
            listener.PeerDisconnectedEvent += (peer, info) => OnDisconnected(peer, info.Reason.ToString());
            listener.NetworkReceiveEvent += (peer, reader, channel, method) =>
            {
                var data = reader.GetRemainingBytes();
                try { OnPacket(peer, data); }
                catch (Exception e) { _log.LogWarning("Bad packet from {Id}: {Err}", peer.Id, e.Message); }
            };

            if (!_net.Start(_opt.UdpPort))
            {
                _log.LogError("Could not bind UDP port {Port}", _opt.UdpPort);
                return;
            }
            _log.LogInformation("Game server listening on UDP {Port}", _opt.UdpPort);

            double acc = 0;
            double last = sw.Elapsed.TotalSeconds;
            var tickSw = new Stopwatch();
            while (!stop.IsCancellationRequested)
            {
                _net.PollEvents();
                double now = sw.Elapsed.TotalSeconds;
                acc += now - last;
                last = now;
                if (acc > 0.25) acc = 0.25;
                while (acc >= GameConfig.Dt)
                {
                    acc -= GameConfig.Dt;
                    tickSw.Restart();
                    Tick(GameConfig.Dt);
                    LastTickMs = tickSw.Elapsed.TotalMilliseconds;
                }
                Thread.Sleep(1);
            }
            _net.Stop();
        }

        // ------------------------------------------------------------------ packets

        private void OnPacket(NetPeer peer, byte[] data)
        {
            if (data.Length == 0 || !_clients.TryGetValue(peer.Id, out var c)) return;
            var r = new ByteReader(data, 1, data.Length - 1);
            switch ((Msg)data[0])
            {
                case Msg.Hello:
                {
                    var h = Protocol.ReadHello(r);
                    if (h.Protocol != GameConfig.ProtocolVersion)
                    {
                        _w.Reset(); Protocol.WriteReject(_w, $"Version mismatch (server {GameConfig.ProtocolVersion}, client {h.Protocol})");
                        Send(c, DeliveryMethod.ReliableOrdered);
                        peer.Disconnect();
                        return;
                    }
                    c.Name = Database.Sanitize(h.Name);
                    c.Look = h.Look;
                    c.ProfileId = _db.Exists(h.ProfileId) ? h.ProfileId : "";
                    c.Welcomed = true;
                    if (_hostClientId < 0 || !_clients.ContainsKey(_hostClientId)) _hostClientId = c.ClientId;
                    _w.Reset(); Protocol.WriteWelcome(_w, c.ClientId, _opt.Name);
                    Send(c, DeliveryMethod.ReliableOrdered);
                    _log.LogInformation("{Name} joined (client {Id}, profile '{Profile}')", c.Name, c.ClientId, c.ProfileId);
                    BroadcastLobby();
                    break;
                }
                case Msg.Ready:
                {
                    c.Ready = r.Bool();
                    c.PreferredSeconds = r.I32();
                    BroadcastLobby();
                    break;
                }
                case Msg.Input:
                {
                    if (_sim == null || c.PlayerId < 0) return;
                    Protocol.ReadInputs(r, _inputScratch);
                    foreach (var cmd in _inputScratch) _sim.SubmitInput(c.PlayerId, cmd);
                    break;
                }
                case Msg.Leave:
                    peer.Disconnect();
                    break;
            }
        }

        private void OnDisconnected(NetPeer peer, string reason)
        {
            if (!_clients.TryGetValue(peer.Id, out var c)) return;
            _clients.Remove(peer.Id);
            _log.LogInformation("{Name} left ({Reason})", c.Name, reason);
            if (_sim != null && c.PlayerId >= 0 && !_sim.Ended)
            {
                // a bot takes over so the match stays full
                var p = _sim.Players[c.PlayerId];
                p.Connected = false;
                p.BotKind = BotKind.Explorer;
                _sim.Bots.Add(new BotBrain(_sim, p, Environment.TickCount));
            }
            if (_hostClientId == c.ClientId) _hostClientId = _clients.Values.Where(x => x.Welcomed).Select(x => x.ClientId).DefaultIfEmpty(-1).First();
            BroadcastLobby();
        }

        private void Send(Client c, DeliveryMethod method, byte channel = 1)
        {
            if (method == DeliveryMethod.Unreliable || method == DeliveryMethod.Sequenced)
            {
                if (_w.Length > c.Peer.GetMaxSinglePacketSize(method)) method = DeliveryMethod.ReliableUnordered;
            }
            c.Peer.Send(_w.Buffer, 0, _w.Length, channel, method);
        }

        // ------------------------------------------------------------------ lobby

        private IEnumerable<Client> Humans => _clients.Values.Where(c => c.Welcomed);

        private void BroadcastLobby()
        {
            var m = new LobbyMsg
            {
                Status = _status,
                Countdown = _status == LobbyStatus.Countdown ? _countdown : _status == LobbyStatus.Results ? _resultsTimer : 0,
                MatchSeconds = ChosenMatchSeconds(),
                TotalPlayers = _opt.TotalPlayers,
                ServerName = _opt.Name,
            };
            foreach (var c in Humans)
                m.Entries.Add(new LobbyEntry { ClientId = c.ClientId, Name = c.Name, Look = c.Look, Ready = c.Ready, IsHost = c.ClientId == _hostClientId });
            _w.Reset();
            Protocol.WriteLobby(_w, m);
            foreach (var c in Humans) Send(c, DeliveryMethod.ReliableOrdered);
            PublicHumans = m.Entries.Count;
            PublicStatus = (int)_status;
        }

        private int ChosenMatchSeconds()
        {
            if (_clients.TryGetValue(_hostClientId, out var host) && host.PreferredSeconds >= 60 && host.PreferredSeconds <= 900)
                return host.PreferredSeconds;
            return _opt.MatchSeconds;
        }

        private void Tick(float dt)
        {
            _lobbyBroadcastT -= dt;
            switch (_status)
            {
                case LobbyStatus.Waiting:
                {
                    var humans = Humans.ToList();
                    if (humans.Count >= _opt.MinHumans && humans.All(h => h.Ready))
                    {
                        _status = LobbyStatus.Countdown;
                        _countdown = _opt.LobbyCountdown;
                        BroadcastLobby();
                    }
                    else if (_lobbyBroadcastT <= 0) { BroadcastLobby(); _lobbyBroadcastT = 1f; }
                    break;
                }
                case LobbyStatus.Countdown:
                {
                    var humans = Humans.ToList();
                    if (humans.Count < _opt.MinHumans || !humans.All(h => h.Ready))
                    {
                        _status = LobbyStatus.Waiting;
                        BroadcastLobby();
                        break;
                    }
                    _countdown -= dt;
                    if (_lobbyBroadcastT <= 0) { BroadcastLobby(); _lobbyBroadcastT = 0.5f; }
                    if (_countdown <= 0) StartMatch();
                    break;
                }
                case LobbyStatus.InMatch:
                    TickMatch();
                    break;
                case LobbyStatus.Results:
                    _resultsTimer -= dt;
                    if (_lobbyBroadcastT <= 0) { BroadcastLobby(); _lobbyBroadcastT = 1f; }
                    if (_resultsTimer <= 0)
                    {
                        _status = LobbyStatus.Waiting;
                        foreach (var c in _clients.Values) { c.Ready = false; c.PlayerId = -1; }
                        _sim = null;
                        BroadcastLobby();
                    }
                    break;
            }
        }

        // ------------------------------------------------------------------ match

        private void StartMatch()
        {
            var settings = new MatchSettings
            {
                MatchSeconds = ChosenMatchSeconds(),
                TotalPlayers = _opt.TotalPlayers,
                Seed = Environment.TickCount & 0x7fffffff,
            };
            _sim = new MatchSim(_map, settings);
            _profileByPlayer.Clear();
            foreach (var c in Humans)
            {
                var p = _sim.AddPlayer(c.Name, c.Look, false);
                c.PlayerId = p.Id;
                _profileByPlayer[p.Id] = c.ProfileId;
            }
            _sim.FillBots();
            _sim.Start();

            foreach (var c in Humans)
            {
                var ms = new MatchStartMsg { Seed = settings.Seed, MatchSeconds = settings.MatchSeconds, YourPlayerId = c.PlayerId };
                foreach (var p in _sim.Players) ms.Roster.Add(new RosterEntry { Id = p.Id, Name = p.Name, Look = p.Look, IsBot = p.IsBot });
                _w.Reset();
                Protocol.WriteMatchStart(_w, ms);
                Send(c, DeliveryMethod.ReliableOrdered);
            }
            SendEvents();
            _status = LobbyStatus.InMatch;
            PublicStatus = (int)_status;
            _log.LogInformation("Match started: {Humans} humans + {Bots} bots, {Secs}s", _profileByPlayer.Count, _sim.Players.Count - _profileByPlayer.Count, settings.MatchSeconds);
        }

        private void TickMatch()
        {
            _sim.Step();
            SendEvents();
            if (_sim.Tick % GameConfig.SnapshotEveryTicks == 0 || _sim.Ended)
            {
                foreach (var c in Humans)
                {
                    if (c.PlayerId < 0) continue;
                    SnapshotBuilder.Build(_sim, _sim.Players[c.PlayerId], _snap);
                    if (_snap.Projectiles.Count > 40) _snap.Projectiles.RemoveRange(40, _snap.Projectiles.Count - 40);
                    _w.Reset();
                    Protocol.WriteSnapshot(_w, _snap);
                    Send(c, DeliveryMethod.Sequenced, 0);
                }
            }
            PublicMatchSecondsLeft = (int)_sim.TimeLeft;

            if (_sim.Ended)
            {
                _w.Reset();
                Protocol.WriteMatchEnd(_w, _sim.Results);
                foreach (var c in Humans) if (c.PlayerId >= 0) Send(c, DeliveryMethod.ReliableOrdered);
                try
                {
                    long id = _db.RecordMatch((int)_sim.Duration, _sim.Results, _profileByPlayer);
                    _log.LogInformation("Match {Id} ended. Winner: {Winner} ({Score})", id, _sim.Results[0].Name, _sim.Results[0].Total);
                }
                catch (Exception e) { _log.LogError("Failed to record match: {Err}", e.Message); }
                Interlocked.Increment(ref MatchesPlayed);
                _status = LobbyStatus.Results;
                _resultsTimer = 8f;
                BroadcastLobby();
            }
        }

        private void SendEvents()
        {
            if (_sim.Events.Count == 0) return;
            foreach (var c in Humans)
            {
                if (c.PlayerId < 0) continue;
                var viewer = _sim.Players[c.PlayerId];
                _eventScratch.Clear();
                foreach (var e in _sim.Events)
                {
                    var ev = e;
                    if (SnapshotBuilder.FilterEvent(_sim, viewer, ref ev)) _eventScratch.Add(ev);
                }
                if (_eventScratch.Count == 0) continue;
                _w.Reset();
                Protocol.WriteEvents(_w, _eventScratch);
                Send(c, DeliveryMethod.ReliableOrdered);
            }
        }

        // ------------------------------------------------------------------ admin

        public object Describe() => new
        {
            name = _opt.Name,
            host = _opt.PublicHost,
            port = _opt.UdpPort,
            status = StatusName,
            humans = PublicHumans,
            maxPlayers = _opt.TotalPlayers,
            matchSecondsLeft = PublicStatus == (int)LobbyStatus.InMatch ? PublicMatchSecondsLeft : 0,
            matchesPlayed = Interlocked.Read(ref MatchesPlayed),
            lastTickMs = Math.Round(LastTickMs, 3),
        };
    }
}
