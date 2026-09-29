using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LiteNetLib;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Veil.Sim;

namespace Veil.Server
{
    /// <summary>
    /// The Gateway over UDP (LiteNetLib reliable-ordered channel) for networks / tunnels that only pass UDP
    /// (e.g. playit.gg free tunnels). Same JSON envelopes as the WebSocket; each datagram = one envelope.
    /// </summary>
    public sealed class UdpGateway : BackgroundService
    {
        private sealed class Conn : GatewayConnection
        {
            public readonly NetPeer Peer;
            public Conn(NetPeer peer) { Peer = peer; }
            public readonly ConcurrentQueue<byte[]> Pending = new ConcurrentQueue<byte[]>();
            public override void Send(GwEnvelope env) => Pending.Enqueue(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(env, Json)));
            public override void Close(string reason)
            {
                CloseReason = reason;
                Send(new GwEnvelope { t = Gw.Kicked, d = JsonSerializer.Serialize(new GwText { text = reason }, Json) });
                CloseAt = Environment.TickCount64 + 600;   // let the reason arrive before disconnecting
            }
            public long CloseAt;
        }

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true };
        private readonly ServerOptions _opt;
        private readonly SocialHub _hub;
        private readonly ILogger<UdpGateway> _log;
        private readonly Dictionary<int, Conn> _conns = new Dictionary<int, Conn>();
        private NetManager _net;

        public UdpGateway(ServerOptions opt, SocialHub hub, ILogger<UdpGateway> log) { _opt = opt; _hub = hub; _log = log; }

        public int Connections => _conns.Count;

        protected override Task ExecuteAsync(CancellationToken stop) =>
            Task.Factory.StartNew(() => Loop(stop), stop, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        private void Loop(CancellationToken stop)
        {
            var listener = new EventBasedNetListener();
            _net = new NetManager(listener) { AutoRecycle = true, ChannelsCount = 1, DisconnectTimeout = 15000, UpdateTime = 15 };
            listener.ConnectionRequestEvent += req => req.AcceptIfKey(Gw.UdpKey);
            listener.PeerConnectedEvent += peer => { lock (_conns) _conns[peer.Id] = new Conn(peer); };
            listener.PeerDisconnectedEvent += (peer, info) =>
            {
                Conn c;
                lock (_conns) { if (!_conns.Remove(peer.Id, out c)) return; }
                _hub.Disconnected(c);
            };
            listener.NetworkReceiveEvent += (peer, reader, channel, method) =>
            {
                var bytes = reader.GetRemainingBytes();
                Conn c;
                lock (_conns) { if (!_conns.TryGetValue(peer.Id, out c)) return; }
                try
                {
                    var env = JsonSerializer.Deserialize<GwEnvelope>(Encoding.UTF8.GetString(bytes), Json);
                    if (env == null) return;
                    if (c.Session == null)
                    {
                        if (env.t != Gw.Hello) { c.Close("say hello first"); return; }
                        var hello = string.IsNullOrEmpty(env.d) ? null : JsonSerializer.Deserialize<GwHello>(env.d, Json);
                        if (hello != null && hello.version != Gw.Version) { c.Close("Update the game (gateway version mismatch)"); return; }
                        if (_hub.Connect(c, hello, out var err) == null) c.Close(err);
                        return;
                    }
                    _hub.Handle(c, env);
                }
                catch (Exception e) { _log.LogWarning("UDP gateway packet from {Peer}: {Err}", peer, e.Message); }
            };
            // a restarting server may still be releasing the port: retry for a few seconds
            bool bound = false;
            for (int i = 0; i < 20 && !bound && !stop.IsCancellationRequested; i++) { bound = _net.Start(_opt.GatewayUdpPort); if (!bound) Thread.Sleep(500); }
            if (!bound) { _log.LogError("UDP gateway could not bind {Port}", _opt.GatewayUdpPort); return; }
            _log.LogInformation("Gateway (UDP) listening on {Port}", _opt.GatewayUdpPort);
            var tmp = new List<Conn>();
            while (!stop.IsCancellationRequested)
            {
                _net.PollEvents();
                tmp.Clear();
                lock (_conns) tmp.AddRange(_conns.Values);
                foreach (var c in tmp)
                {
                    while (c.Pending.TryDequeue(out var msg)) c.Peer.Send(msg, DeliveryMethod.ReliableOrdered);
                    if (c.CloseAt > 0 && Environment.TickCount64 > c.CloseAt) { c.CloseAt = 0; c.Peer.Disconnect(); }
                }
                Thread.Sleep(5);
            }
            _net.Stop();
        }
    }
}
