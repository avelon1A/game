using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Concentus.Enums;
using Concentus.Structs;
using UnityEngine;
using Veil.Sim;

namespace Veil.Voice
{
    public enum VoiceMode { PushToTalk = 0, OpenMic = 1, Off = 2 }

    /// <summary>What the UI needs from voice chat. The self-hosted relay implements it today; Vivox could later.</summary>
    public interface IVoiceService
    {
        VoiceMode Mode { get; set; }
        bool MicMuted { get; set; }
        bool Deafened { get; set; }
        bool PushToTalkHeld { get; set; }
        float OutputVolume { get; set; }
        float Sensitivity { get; set; }     // open-mic threshold 0..1
        bool Connected { get; }
        bool LocalSpeaking { get; }
        float InputLevel { get; }
        string Channel { get; }
        string Error { get; }
        void JoinChannel(string host, int port, string channel, string token, string profileId);
        void LeaveChannel();
        bool IsSpeaking(string profileId);
        bool IsMuted(string profileId);
        void SetMuted(string profileId, bool muted);
        float GetVolume(string profileId);
        void SetVolume(string profileId, float v);
        void Update(float dt);
    }

    /// <summary>
    /// Squad / party voice over the VEIL voice relay: Unity Microphone → 16 kHz mono → Opus (Concentus, pure C#) →
    /// UDP relay → Opus decode per speaker → jitter buffer → streaming AudioSource. Only frames for your channel reach
    /// you (the relay enforces it), so enemy squads can never hear you.
    /// </summary>
    public sealed class VoiceChat : IVoiceService, IDisposable
    {
        private sealed class Speaker
        {
            public byte Slot;
            public string ProfileId = "";
            public OpusDecoder Decoder;
            public readonly float[] Ring = new float[VoiceWire.SampleRate];   // 1 s
            public int Write, Read, Count;
            public bool Playing;
            public float LastPacket = -10;
            public AudioSource Source;
            public ushort LastSeq;
            public readonly object Lock = new object();
        }

        public VoiceMode Mode { get; set; } = VoiceMode.PushToTalk;
        public bool MicMuted { get; set; }
        public bool Deafened { get; set; }
        public bool PushToTalkHeld { get; set; }
        public float OutputVolume { get; set; } = 1f;
        public float Sensitivity { get; set; } = 0.35f;
        public bool Connected => _joined && Time.unscaledTime - _lastServer < 6f;
        public bool LocalSpeaking { get; private set; }
        public float InputLevel { get; private set; }
        public string Channel { get; private set; } = "";
        public string Error { get; private set; } = "";

        private UdpClient _udp;
        private Thread _rx;
        private volatile bool _running;
        private readonly ConcurrentQueue<byte[]> _inbox = new ConcurrentQueue<byte[]>();
        private IPEndPoint _server;
        private string _token = "", _profile = "";
        private bool _joined;
        private float _joinT, _lastServer = -100;
        private readonly Dictionary<byte, Speaker> _speakers = new Dictionary<byte, Speaker>();
        private readonly Dictionary<string, byte> _slotOf = new Dictionary<string, byte>();
        private readonly HashSet<string> _muted = new HashSet<string>();
        private readonly Dictionary<string, float> _volume = new Dictionary<string, float>();
        private readonly ByteWriter _w = new ByteWriter(VoiceWire.MaxPacket + 16);
        private readonly Transform _root;

        // capture
        private AudioClip _mic;
        private string _micDevice;
        private int _micRate, _micPos;
        private bool _micWanted, _permissionAsked;
        private readonly List<float> _pcm16k = new List<float>(4096);
        private float _resamplePhase;
        private float[] _micBuf = new float[4096];
        private OpusEncoder _encoder;
        private readonly short[] _frame = new short[VoiceWire.FrameSamples];
        private readonly byte[] _opus = new byte[VoiceWire.MaxPacket];
        private ushort _seq;
        private float _hangover;

        public VoiceChat()
        {
            var go = new GameObject("Voice");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _root = go.transform;
        }

        // ------------------------------------------------------------------ channel

        public void JoinChannel(string host, int port, string channel, string token, string profileId)
        {
            if (Mode == VoiceMode.Off) { LeaveChannel(); return; }
            if (channel == Channel && _server != null && token == _token) return;
            LeaveChannel();
            try
            {
                var addr = Dns.GetHostAddresses(host);
                IPAddress ip = null;
                foreach (var a in addr) if (a.AddressFamily == AddressFamily.InterNetwork) { ip = a; break; }
                if (ip == null) { Error = "voice host not found"; return; }
                _server = new IPEndPoint(ip, port);
                _udp = new UdpClient(0);
                _running = true;
                _rx = new Thread(ReceiveLoop) { IsBackground = true, Name = "VoiceRx" };
                _rx.Start();
            }
            catch (Exception e) { Error = e.Message; return; }
            Channel = channel; _token = token; _profile = profileId;
            _joinT = 0; Error = "";
        }

        public void LeaveChannel()
        {
            if (_udp != null && _server != null)
            {
                try { _w.Reset(); _w.U8(VoiceWire.Leave); _udp.Send(_w.Buffer, _w.Length, _server); } catch { }
            }
            _running = false;
            try { _udp?.Close(); } catch { }
            _udp = null; _server = null; _joined = false;
            Channel = "";
            foreach (var s in _speakers.Values) if (s.Source != null) UnityEngine.Object.Destroy(s.Source.gameObject);
            _speakers.Clear(); _slotOf.Clear();
            StopMic();
        }

        private void ReceiveLoop()
        {
            var udp = _udp;
            while (_running && udp != null)
            {
                try
                {
                    IPEndPoint any = null;
                    var d = udp.Receive(ref any);
                    if (d != null && d.Length > 0) _inbox.Enqueue(d);
                }
                catch (SocketException) { if (!_running) break; }
                catch (ObjectDisposedException) { break; }
            }
        }

        // ------------------------------------------------------------------ frame loop (main thread)

        public void Update(float dt)
        {
            if (_udp == null) { LocalSpeaking = false; return; }
            _joinT -= dt;
            if (_joinT <= 0)
            {
                _joinT = 2f;
                VoiceWire.WriteJoin(_w, Channel, _profile, _token);
                SendW();
            }
            while (_inbox.TryDequeue(out var d)) Handle(d);
            Capture(dt);
            foreach (var s in _speakers.Values)
                if (s.Source != null) s.Source.volume = Deafened ? 0 : OutputVolume * GetVolume(s.ProfileId) * (IsMuted(s.ProfileId) ? 0 : 1);
        }

        private void Handle(byte[] d)
        {
            var r = new ByteReader(d, 1, d.Length - 1);
            switch (d[0])
            {
                case VoiceWire.Joined: _joined = true; _lastServer = Time.unscaledTime; break;
                case VoiceWire.Denied: Error = r.Str(); _joined = false; break;
                case VoiceWire.Roster:
                {
                    _lastServer = Time.unscaledTime;
                    _slotOf.Clear();
                    var roster = VoiceWire.ReadRoster(r);
                    var live = new HashSet<byte>();
                    foreach (var (slot, id) in roster)
                    {
                        _slotOf[id] = slot;
                        live.Add(slot);
                        if (id == _profile) continue;
                        if (!_speakers.TryGetValue(slot, out var sp) || sp.ProfileId != id)
                        {
                            if (sp?.Source != null) UnityEngine.Object.Destroy(sp.Source.gameObject);
                            _speakers[slot] = NewSpeaker(slot, id);
                        }
                    }
                    foreach (var slot in new List<byte>(_speakers.Keys))
                        if (!live.Contains(slot)) { if (_speakers[slot].Source != null) UnityEngine.Object.Destroy(_speakers[slot].Source.gameObject); _speakers.Remove(slot); }
                    break;
                }
                case VoiceWire.FrameOut:
                {
                    _lastServer = Time.unscaledTime;
                    byte slot = r.U8();
                    ushort seq = r.U16();
                    if (!_speakers.TryGetValue(slot, out var sp)) return;
                    sp.LastPacket = Time.unscaledTime;
                    if (Deafened || IsMuted(sp.ProfileId)) return;
                    if ((short)(seq - sp.LastSeq) <= 0 && sp.LastSeq != 0) return;   // late / duplicate
                    sp.LastSeq = seq;
                    int n;
                    try { n = sp.Decoder.Decode(d, r.Position, r.Remaining, _decodeBuf, 0, VoiceWire.FrameSamples, false); }
                    catch { return; }
                    lock (sp.Lock)
                    {
                        for (int i = 0; i < n; i++)
                        {
                            if (sp.Count >= sp.Ring.Length) { sp.Read = (sp.Read + 1) % sp.Ring.Length; sp.Count--; }   // overflow: drop oldest
                            sp.Ring[sp.Write] = _decodeBuf[i] / 32768f;
                            sp.Write = (sp.Write + 1) % sp.Ring.Length;
                            sp.Count++;
                        }
                        // keep latency bounded: if more than 300 ms buffered, skip ahead
                        int max = VoiceWire.SampleRate * 3 / 10;
                        if (sp.Count > max) { int drop = sp.Count - max / 2; sp.Read = (sp.Read + drop) % sp.Ring.Length; sp.Count -= drop; }
                    }
                    break;
                }
            }
        }

        private readonly short[] _decodeBuf = new short[VoiceWire.FrameSamples * 3];

        private Speaker NewSpeaker(byte slot, string id)
        {
            var sp = new Speaker { Slot = slot, ProfileId = id, Decoder = OpusDecoder.Create(VoiceWire.SampleRate, 1) };
            var go = new GameObject("Speaker_" + slot);
            go.transform.SetParent(_root, false);
            var src = go.AddComponent<AudioSource>();
            src.spatialBlend = 0;
            src.loop = true;
            src.playOnAwake = false;
            src.clip = AudioClip.Create("voice" + slot, VoiceWire.SampleRate, 1, VoiceWire.SampleRate, true, data => Fill(sp, data));
            src.Play();
            sp.Source = src;
            return sp;
        }

        /// <summary>Audio thread: pull from the jitter buffer (starts once 60 ms are buffered, silence on underrun).</summary>
        private static void Fill(Speaker sp, float[] data)
        {
            lock (sp.Lock)
            {
                if (!sp.Playing && sp.Count >= VoiceWire.SampleRate * 6 / 100) sp.Playing = true;
                for (int i = 0; i < data.Length; i++)
                {
                    if (sp.Playing && sp.Count > 0)
                    {
                        data[i] = sp.Ring[sp.Read];
                        sp.Read = (sp.Read + 1) % sp.Ring.Length;
                        sp.Count--;
                    }
                    else { data[i] = 0; sp.Playing = false; }
                }
            }
        }

        // ------------------------------------------------------------------ capture

        private void Capture(float dt)
        {
            bool wantTalk = !MicMuted && _joined && Mode != VoiceMode.Off && (Mode == VoiceMode.OpenMic || PushToTalkHeld);
            // the microphone is only open while you could be heard (PTT held / open mic, not muted)
            _micWanted = wantTalk;
            if (_micWanted && _mic == null) StartMic();
            if (!_micWanted && _mic != null) StopMic();
            if (_mic == null) { LocalSpeaking = false; InputLevel = Mathf.MoveTowards(InputLevel, 0, dt * 3); return; }

            int pos = Microphone.GetPosition(_micDevice);
            int total = _mic.samples;
            int avail = (pos - _micPos + total) % total;
            if (avail <= 0) return;
            if (_micBuf.Length < avail) _micBuf = new float[avail];
            // read (handles wrap)
            int first = Mathf.Min(avail, total - _micPos);
            var tmp = new float[first];
            _mic.GetData(tmp, _micPos);
            Array.Copy(tmp, 0, _micBuf, 0, first);
            if (avail > first)
            {
                var tmp2 = new float[avail - first];
                _mic.GetData(tmp2, 0);
                Array.Copy(tmp2, 0, _micBuf, first, avail - first);
            }
            _micPos = pos;

            // resample to 16 kHz (linear)
            float step = _micRate / (float)VoiceWire.SampleRate;
            float peak = 0;
            while (_resamplePhase < avail - 1)
            {
                int i = (int)_resamplePhase;
                float f = _resamplePhase - i;
                float v = _micBuf[i] * (1 - f) + _micBuf[i + 1] * f;
                _pcm16k.Add(v);
                peak = Mathf.Max(peak, Mathf.Abs(v));
                _resamplePhase += step;
            }
            _resamplePhase -= avail;
            if (_resamplePhase < 0) _resamplePhase = 0;
            InputLevel = Mathf.Lerp(InputLevel, Mathf.Clamp01(peak * 3f), 0.5f);

            while (_pcm16k.Count >= VoiceWire.FrameSamples)
            {
                float rms = 0;
                for (int i = 0; i < VoiceWire.FrameSamples; i++)
                {
                    float v = _pcm16k[i];
                    rms += v * v;
                    _frame[i] = (short)Mathf.Clamp(v * 32767f, -32768f, 32767f);
                }
                _pcm16k.RemoveRange(0, VoiceWire.FrameSamples);
                rms = Mathf.Sqrt(rms / VoiceWire.FrameSamples);
                bool voice = Mode == VoiceMode.PushToTalk ? PushToTalkHeld : rms > Mathf.Lerp(0.004f, 0.08f, Sensitivity);
                if (voice) _hangover = 0.35f; else _hangover -= VoiceWire.FrameMs / 1000f;
                bool send = wantTalk && (voice || (Mode == VoiceMode.OpenMic && _hangover > 0));
                LocalSpeaking = send;
                if (!send) continue;
                int len;
                try { len = _encoder.Encode(_frame, 0, VoiceWire.FrameSamples, _opus, 0, _opus.Length); }
                catch { continue; }
                _w.Reset(); _w.U8(VoiceWire.Frame); _w.U16(++_seq); _w.Bytes(_opus, 0, len);
                SendW();
            }
        }

        private void StartMic()
        {
            if (!EnsurePermission()) return;
            if (Microphone.devices.Length == 0) { Error = "No microphone"; return; }
            _micDevice = null;   // default device
            Microphone.GetDeviceCaps(_micDevice, out int minF, out int maxF);
            _micRate = (minF == 0 && maxF == 0) ? 16000 : Mathf.Clamp(16000, minF, maxF);
            _mic = Microphone.Start(_micDevice, true, 1, _micRate);
            if (_mic == null) { Error = "Microphone unavailable"; return; }
            _micRate = _mic.frequency;
            _micPos = 0;
            _resamplePhase = 0;
            _pcm16k.Clear();
            if (_encoder == null)
            {
                _encoder = OpusEncoder.Create(VoiceWire.SampleRate, 1, OpusApplication.OPUS_APPLICATION_VOIP);
                _encoder.Bitrate = 20000;
                _encoder.Complexity = Application.isMobilePlatform ? 3 : 6;
                _encoder.UseVBR = true;
            }
        }

        private void StopMic()
        {
            if (_mic == null) return;
            Microphone.End(_micDevice);
            _mic = null;
            LocalSpeaking = false;
        }

        private bool EnsurePermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
            {
                if (!_permissionAsked) { _permissionAsked = true; UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone); }
                return false;
            }
#elif UNITY_IOS && !UNITY_EDITOR
            if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
            {
                if (!_permissionAsked) { _permissionAsked = true; Application.RequestUserAuthorization(UserAuthorization.Microphone); }
                return false;
            }
#endif
            return true;
        }

        private void SendW()
        {
            try { _udp?.Send(_w.Buffer, _w.Length, _server); } catch { }
        }

        // ------------------------------------------------------------------ queries

        public bool IsSpeaking(string profileId)
        {
            if (profileId == _profile) return LocalSpeaking;
            if (!_slotOf.TryGetValue(profileId, out var slot) || !_speakers.TryGetValue(slot, out var sp)) return false;
            return Time.unscaledTime - sp.LastPacket < 0.3f && !IsMuted(profileId);
        }

        public bool IsMuted(string profileId) => _muted.Contains(profileId);
        public void SetMuted(string profileId, bool muted) { if (muted) _muted.Add(profileId); else _muted.Remove(profileId); }
        public float GetVolume(string profileId) => _volume.TryGetValue(profileId, out var v) ? v : 1f;
        public void SetVolume(string profileId, float v) => _volume[profileId] = Mathf.Clamp(v, 0, 2);
        public bool InChannel(string profileId) => _slotOf.ContainsKey(profileId);

        public void Dispose() => LeaveChannel();
    }
}
