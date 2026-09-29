using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Veil.Sim;

namespace Veil.Server
{
    /// <summary>
    /// Squad / party voice relay (SFU-style forwarder). Clients send Opus frames; the relay forwards each frame to the
    /// other members of the sender's channel only — never to other squads. Channel membership is proven with a token
    /// from the Gateway and re-checked against live party / match state. No audio is decoded or stored here.
    /// </summary>
    public sealed class VoiceRelay : BackgroundService
    {
        private sealed class Member
        {
            public IPEndPoint Ep;
            public string ProfileId = "", Channel = "";
            public byte Slot;
            public DateTime LastSeen;
        }

        private readonly ServerOptions _opt;
        private readonly SocialHub _hub;
        private readonly ILogger<VoiceRelay> _log;
        private readonly Dictionary<string, Member> _byEp = new Dictionary<string, Member>();
        private readonly Dictionary<string, List<Member>> _channels = new Dictionary<string, List<Member>>();
        private readonly ByteWriter _w = new ByteWriter(VoiceWire.MaxPacket + 16);
        private UdpClient _udp;
        public long FramesForwarded;

        public VoiceRelay(ServerOptions opt, SocialHub hub, ILogger<VoiceRelay> log) { _opt = opt; _hub = hub; _log = log; }

        public int Speakers => _byEp.Count;

        protected override async Task ExecuteAsync(CancellationToken stop)
        {
            for (int i = 0; _udp == null; i++)
            {
                try { _udp = new UdpClient(new IPEndPoint(IPAddress.Any, _opt.VoicePort)); }
                catch (Exception e)
                {
                    if (i >= 20 || stop.IsCancellationRequested) { _log.LogError("Voice relay could not bind UDP {Port}: {Err}", _opt.VoicePort, e.Message); return; }
                    await Task.Delay(500, stop).ContinueWith(_ => { });
                }
            }
            _log.LogInformation("Voice relay listening on UDP {Port}", _opt.VoicePort);
            var sweep = DateTime.UtcNow;
            while (!stop.IsCancellationRequested)
            {
                UdpReceiveResult r;
                try { r = await _udp.ReceiveAsync(stop); }
                catch (OperationCanceledException) { break; }
                catch (SocketException) { continue; }   // ICMP port unreachable from a gone client
                try { OnPacket(r.RemoteEndPoint, r.Buffer); }
                catch (Exception e) { _log.LogDebug("voice packet: {Err}", e.Message); }
                if ((DateTime.UtcNow - sweep).TotalSeconds > 2) { sweep = DateTime.UtcNow; Sweep(); }
            }
            _udp.Dispose();
        }

        private void OnPacket(IPEndPoint ep, byte[] data)
        {
            if (data.Length == 0 || data.Length > VoiceWire.MaxPacket + 16) return;
            string key = ep.ToString();
            var r = new ByteReader(data, 1, data.Length - 1);
            switch (data[0])
            {
                case VoiceWire.Join:
                {
                    string channel = r.Str(), profile = r.Str(), token = r.Str();
                    if (token != _hub.VoiceToken(profile, channel) || !_hub.MayUseVoice(profile, channel)) { Deny(ep, "not allowed on this channel"); return; }
                    if (_byEp.TryGetValue(key, out var m) && m.Channel == channel) { m.LastSeen = DateTime.UtcNow; return; }   // keep-alive
                    if (m != null) Remove(m);
                    // same profile from a new address (phone switched network): replace the old endpoint
                    foreach (var old in _byEp.Values.Where(x => x.ProfileId == profile).ToList()) Remove(old);
                    if (!_channels.TryGetValue(channel, out var list)) _channels[channel] = list = new List<Member>();
                    byte slot = 0;
                    while (list.Any(x => x.Slot == slot)) slot++;
                    m = new Member { Ep = ep, ProfileId = profile, Channel = channel, Slot = slot, LastSeen = DateTime.UtcNow };
                    list.Add(m);
                    _byEp[key] = m;
                    _w.Reset(); _w.U8(VoiceWire.Joined); _w.U8(slot);
                    Send(ep);
                    BroadcastRoster(channel);
                    break;
                }
                case VoiceWire.Frame:
                {
                    if (!_byEp.TryGetValue(key, out var m)) return;
                    m.LastSeen = DateTime.UtcNow;
                    ushort seq = r.U16();
                    int len = r.Remaining;
                    if (len <= 0) return;
                    _w.Reset(); _w.U8(VoiceWire.FrameOut); _w.U8(m.Slot); _w.U16(seq); _w.Bytes(data, r.Position, len);
                    foreach (var o in _channels[m.Channel]) if (o != m) Send(o.Ep);
                    Interlocked.Increment(ref FramesForwarded);
                    break;
                }
                case VoiceWire.Leave:
                    if (_byEp.TryGetValue(key, out var lm)) Remove(lm);
                    break;
            }
        }

        private void Sweep()
        {
            var now = DateTime.UtcNow;
            foreach (var m in _byEp.Values.ToList())
                if ((now - m.LastSeen).TotalSeconds > 6 || !_hub.MayUseVoice(m.ProfileId, m.Channel)) Remove(m);
        }

        private void Remove(Member m)
        {
            _byEp.Remove(m.Ep.ToString());
            if (_channels.TryGetValue(m.Channel, out var list))
            {
                list.Remove(m);
                if (list.Count == 0) _channels.Remove(m.Channel);
                else BroadcastRoster(m.Channel);
            }
        }

        private void BroadcastRoster(string channel)
        {
            var list = _channels[channel];
            VoiceWire.WriteRoster(_w, list.Select(x => (x.Slot, x.ProfileId)).ToList());
            foreach (var m in list) Send(m.Ep);
        }

        private void Deny(IPEndPoint ep, string reason)
        {
            _w.Reset(); _w.U8(VoiceWire.Denied); _w.Str(reason);
            Send(ep);
        }

        private void Send(IPEndPoint ep)
        {
            try { _udp.Send(_w.Buffer, _w.Length, ep); } catch (SocketException) { }
        }
    }
}
