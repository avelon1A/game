using System;
using System.Collections.Generic;
using LiteNetLib;
using UnityEngine;
using Veil.Sim;

namespace Veil.Net
{
    /// <summary>UDP connection to Veil.Server (LiteNetLib). Poll() must be called every frame.</summary>
    public sealed class VeilNetClient
    {
        public const string ConnectionKey = "VEIL";

        public enum State { Idle, Connecting, Connected, Disconnected }

        public State Status { get; private set; } = State.Idle;
        public string LastError { get; private set; } = "";
        public int ClientId { get; private set; } = -1;
        public string ServerName { get; private set; } = "";
        public LobbyMsg Lobby { get; private set; }
        public int Ping => _peer != null ? _peer.Ping : 0;

        public event Action Welcomed;
        public event Action<LobbyMsg> LobbyUpdated;
        public event Action<MatchStartMsg> MatchStarted;
        public event Action<Snapshot> SnapshotReceived;
        public event Action<List<SimEvent>> EventsReceived;
        public event Action<List<PlayerResult>> MatchEnded;
        public event Action<string> Disconnected;
        /// <summary>A server answered LAN discovery: (ip, port, name, players).</summary>
        public event Action<string, int, string, int> ServerFound;

        private readonly NetManager _net;
        private readonly EventBasedNetListener _listener = new EventBasedNetListener();
        private readonly ByteWriter _w = new ByteWriter(512);
        private NetPeer _peer;
        private HelloMsg _hello;

        public VeilNetClient()
        {
            _net = new NetManager(_listener) { AutoRecycle = true, ChannelsCount = 2, DisconnectTimeout = 8000, UpdateTime = 10, UnconnectedMessagesEnabled = true };
            _listener.NetworkReceiveUnconnectedEvent += (ep, reader, type) =>
            {
                var parts = System.Text.Encoding.UTF8.GetString(reader.GetRemainingBytes()).Split('|');
                if (parts.Length < 3 || parts[0] != "VEIL!") return;
                int.TryParse(parts[2], out int port);
                int players = parts.Length > 3 && int.TryParse(parts[3], out int pl) ? pl : 0;
                var ip = ep.Address.IsIPv4MappedToIPv6 ? ep.Address.MapToIPv4() : ep.Address;
                ServerFound?.Invoke(ip.ToString(), port, parts[1], players);
            };
            _listener.PeerConnectedEvent += p =>
            {
                _peer = p;
                Status = State.Connected;
                _w.Reset();
                Protocol.WriteHello(_w, _hello);
                Send(DeliveryMethod.ReliableOrdered);
            };
            _listener.PeerDisconnectedEvent += (p, info) =>
            {
                Status = State.Disconnected;
                _peer = null;
                if (string.IsNullOrEmpty(LastError)) LastError = info.Reason.ToString();
                Disconnected?.Invoke(LastError);
            };
            _listener.NetworkErrorEvent += (ep, err) => LastError = err.ToString();
            _listener.NetworkReceiveEvent += (p, reader, ch, method) =>
            {
                var data = reader.GetRemainingBytes();
                try { Handle(data); }
                catch (Exception e) { Debug.LogWarning("[VEIL] bad packet: " + e.Message); }
            };
        }

        public void Connect(string host, int port, HelloMsg hello)
        {
            _hello = hello;
            LastError = "";
            Lobby = null;
            if (!_net.IsRunning) _net.Start();
            Status = State.Connecting;
            _net.Connect(host, port, ConnectionKey);
        }

        public void Poll() => _net.PollEvents();

        /// <summary>Broadcast a LAN discovery request; answers arrive through <see cref="ServerFound"/>.</summary>
        public void Discover(int port)
        {
            if (!_net.IsRunning) _net.Start();
            _net.SendBroadcast(System.Text.Encoding.UTF8.GetBytes("VEIL?"), port);
        }

        public void Disconnect()
        {
            if (_peer != null)
            {
                _w.Reset(); _w.U8((byte)Msg.Leave);
                Send(DeliveryMethod.ReliableOrdered);
            }
            _net.DisconnectAll();
            _net.PollEvents();
            _net.Stop();
            Status = State.Idle;
            _peer = null;
        }

        public void SendReady(bool ready, int matchSeconds)
        {
            _w.Reset();
            Protocol.WriteReady(_w, ready, matchSeconds);
            Send(DeliveryMethod.ReliableOrdered);
        }

        public void SendInputs(List<InputCmd> cmds, int start, int count)
        {
            _w.Reset();
            Protocol.WriteInputs(_w, cmds, start, count);
            Send(DeliveryMethod.Unreliable, 0);
        }

        private void Send(DeliveryMethod m, byte channel = 1)
        {
            _peer?.Send(_w.Buffer, 0, _w.Length, channel, m);
        }

        private void Handle(byte[] data)
        {
            if (data.Length == 0) return;
            var r = new ByteReader(data, 1, data.Length - 1);
            switch ((Msg)data[0])
            {
                case Msg.Welcome:
                    ClientId = r.I32();
                    ServerName = r.Str();
                    Welcomed?.Invoke();
                    break;
                case Msg.Reject:
                    LastError = r.Str();
                    break;
                case Msg.Lobby:
                    Lobby = Protocol.ReadLobby(r);
                    LobbyUpdated?.Invoke(Lobby);
                    break;
                case Msg.MatchStart:
                    MatchStarted?.Invoke(Protocol.ReadMatchStart(r));
                    break;
                case Msg.Snapshot:
                    SnapshotReceived?.Invoke(Protocol.ReadSnapshot(r));
                    break;
                case Msg.Events:
                {
                    var list = new List<SimEvent>();
                    Protocol.ReadEvents(r, list);
                    EventsReceived?.Invoke(list);
                    break;
                }
                case Msg.MatchEnd:
                    MatchEnded?.Invoke(Protocol.ReadMatchEnd(r));
                    break;
            }
        }
    }
}
