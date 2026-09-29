using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Veil.Sim;

namespace Veil.Net
{
    /// <summary>
    /// Realtime connection to the VEIL Gateway (WebSocket): presence, friends, party, invites, matchmaking.
    /// Socket I/O runs on background tasks; everything is dispatched on the main thread from <see cref="Update"/>.
    /// Reconnects with backoff and re-sends hello, so parties survive network blips and app restarts.
    /// </summary>
    public sealed class GatewayClient
    {
        public enum State { Offline, Connecting, Online }

        public State Status { get; private set; } = State.Offline;
        public string Handle { get; private set; } = "";
        public string MyId { get; private set; } = "";
        public int PingMs { get; private set; }
        public int VoicePort { get; private set; } = 7778;
        public string Host { get; private set; } = "";
        public string LastError { get; private set; } = "";
        public FriendsState Friends { get; private set; } = new FriendsState();
        public PartyInfo Party { get; private set; } = new PartyInfo();
        public readonly List<IncomingInvite> Invites = new List<IncomingInvite>();
        /// <summary>Current match assignment (kept until the match finishes → REJOIN after leaving).</summary>
        public MatchAssignedMsg Assignment { get; private set; }

        public event Action Changed;                       // friends / party / invites / status changed
        public event Action<IncomingInvite> InviteReceived;
        public event Action<MatchAssignedMsg> MatchAssigned;
        public event Action MatchFinished;
        public event Action<string> Notice;

        private ClientWebSocket _ws;
        private CancellationTokenSource _cts;
        private readonly ConcurrentQueue<string> _inbox = new ConcurrentQueue<string>();
        private readonly ConcurrentQueue<Action> _mainThread = new ConcurrentQueue<Action>();
        private readonly Dictionary<string, Action<GwEnvelope>> _pending = new Dictionary<string, Action<GwEnvelope>>();
        private int _req;
        private string _url, _id, _token, _name, _look;
        private bool _wanted;
        private float _pingT, _retryT, _retryDelay = 1f;
        private long _pingSentMs;

        public bool Online => Status == State.Online;

        public void Connect(string host, int httpPort, string id, string token, string name, string look)
        {
            Host = host;
            _url = $"ws://{host}:{httpPort}/ws";
            _id = id; _token = token; _name = name; _look = look;
            _wanted = true;
            _retryDelay = 1f;
            Open();
        }

        public void Disconnect()
        {
            _wanted = false;
            Close();
            Status = State.Offline;
            Changed?.Invoke();
        }

        private void Open()
        {
            Close();
            Status = State.Connecting;
            Changed?.Invoke();
            var ws = new ClientWebSocket();
            var cts = new CancellationTokenSource();
            _ws = ws; _cts = cts;
            Task.Run(async () =>
            {
                try
                {
                    using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cts.Token))
                    {
                        timeout.CancelAfter(6000);
                        await ws.ConnectAsync(new Uri(_url), timeout.Token);
                    }
                    var hello = new GwHello { id = _id, token = _token, name = _name, look = _look };
                    await SendRawAsync(ws, new GwEnvelope { t = Gw.Hello, d = JsonUtility.ToJson(hello) }, cts.Token);
                    var buf = new byte[32 * 1024];
                    var sb = new StringBuilder();
                    while (ws.State == WebSocketState.Open && !cts.IsCancellationRequested)
                    {
                        sb.Clear();
                        WebSocketReceiveResult r;
                        do
                        {
                            r = await ws.ReceiveAsync(new ArraySegment<byte>(buf), cts.Token);
                            if (r.MessageType == WebSocketMessageType.Close) break;
                            sb.Append(Encoding.UTF8.GetString(buf, 0, r.Count));
                        } while (!r.EndOfMessage);
                        if (r.MessageType == WebSocketMessageType.Close) break;
                        _inbox.Enqueue(sb.ToString());
                    }
                }
                catch (Exception e) { _mainThread.Enqueue(() => LastError = e.Message); }
                _mainThread.Enqueue(() => { if (_ws == ws) OnClosed(); });
            });
        }

        private void Close()
        {
            try { _cts?.Cancel(); } catch { }
            try { _ws?.Abort(); _ws?.Dispose(); } catch { }
            _ws = null;
            _pending.Clear();
        }

        private void OnClosed()
        {
            bool was = Status == State.Online;
            Status = State.Offline;
            _ws = null;
            Changed?.Invoke();
            if (_wanted) { _retryT = was ? 0.5f : _retryDelay; _retryDelay = Mathf.Min(_retryDelay * 2f, 10f); }
        }

        /// <summary>Call every frame (main thread).</summary>
        public void Update(float dt)
        {
            while (_mainThread.TryDequeue(out var a)) a();
            while (_inbox.TryDequeue(out var msg)) Dispatch(msg);
            if (Status == State.Offline && _wanted && _url != null)
            {
                _retryT -= dt;
                if (_retryT <= 0) Open();
            }
            if (Status == State.Online)
            {
                _pingT -= dt;
                if (_pingT <= 0)
                {
                    _pingT = 5f;
                    _pingSentMs = Now();
                    Send(Gw.Ping, new GwPing { clientMs = _pingSentMs, rttMs = PingMs });
                }
            }
        }

        private static long Now() => DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;

        private void Dispatch(string json)
        {
            GwEnvelope env;
            try { env = JsonUtility.FromJson<GwEnvelope>(json); } catch { return; }
            if (env == null) return;
            switch (env.t)
            {
                case Gw.Reply:
                    if (_pending.TryGetValue(env.id, out var cb)) { _pending.Remove(env.id); cb(env); }
                    break;
                case Gw.Welcome:
                {
                    var w = JsonUtility.FromJson<GwWelcome>(env.d);
                    Handle = w.handle; MyId = w.id; VoicePort = w.voicePort > 0 ? w.voicePort : 7778;
                    Status = State.Online;
                    _retryDelay = 1f;
                    LastError = "";
                    _pingT = 0;
                    Changed?.Invoke();
                    break;
                }
                case Gw.Pong:
                {
                    var p = JsonUtility.FromJson<GwPing>(env.d);
                    if (p.clientMs == _pingSentMs) PingMs = (int)Math.Max(1, Now() - _pingSentMs);
                    break;
                }
                case Gw.FriendsState: Friends = JsonUtility.FromJson<FriendsState>(env.d); Changed?.Invoke(); break;
                case Gw.Presence:
                {
                    var p = JsonUtility.FromJson<FriendEntry>(env.d);
                    var f = Friends.friends.Find(x => x.id == p.id);
                    if (f != null) { f.status = p.status; f.detail = p.detail; f.joinable = p.joinable; f.partyCode = p.partyCode; f.look = p.look; f.level = p.level; }
                    Friends.friends.Sort((a, b) => a.status != b.status ? (b.status > 0).CompareTo(a.status > 0) : string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
                    Changed?.Invoke();
                    break;
                }
                case Gw.PartyState: Party = JsonUtility.FromJson<PartyInfo>(env.d); Changed?.Invoke(); break;
                case Gw.Invite:
                {
                    var inv = JsonUtility.FromJson<IncomingInvite>(env.d);
                    Invites.RemoveAll(x => x.inviteId == inv.inviteId);
                    Invites.Add(inv);
                    InviteReceived?.Invoke(inv);
                    Changed?.Invoke();
                    break;
                }
                case Gw.InviteGone:
                {
                    var id = JsonUtility.FromJson<GwId>(env.d).id;
                    Invites.RemoveAll(x => x.inviteId == id);
                    Changed?.Invoke();
                    break;
                }
                case Gw.MatchAssigned:
                    Assignment = JsonUtility.FromJson<MatchAssignedMsg>(env.d);
                    MatchAssigned?.Invoke(Assignment);
                    Changed?.Invoke();
                    break;
                case Gw.MatchFinished:
                    Assignment = null;
                    MatchFinished?.Invoke();
                    Changed?.Invoke();
                    break;
                case Gw.Notice: Notice?.Invoke(JsonUtility.FromJson<GwText>(env.d).text); break;
                case Gw.Kicked:
                {
                    var t = JsonUtility.FromJson<GwText>(env.d).text;
                    Notice?.Invoke(t);
                    if (t.Contains("another device") || t.Contains("Update the game") || t.Contains("credentials")) { _wanted = false; LastError = t; }
                    break;
                }
            }
        }

        // ------------------------------------------------------------------ requests

        /// <summary>Sends a request; <paramref name="done"/> gets (ok, error) on the main thread.</summary>
        public void Request(string type, object payload, Action<bool, string> done = null)
        {
            if (!Online) { done?.Invoke(false, "Not connected"); return; }
            string id = "c" + (++_req);
            if (done != null) _pending[id] = env => done(env.ok, env.err);
            Send(type, payload, id);
        }

        private void Send(string type, object payload, string id = "")
        {
            var ws = _ws;
            if (ws == null || ws.State != WebSocketState.Open) return;
            var env = new GwEnvelope { t = type, id = id, d = payload == null ? "" : JsonUtility.ToJson(payload) };
            var cts = _cts;
            Task.Run(() => SendRawAsync(ws, env, cts.Token));
        }

        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

        private async Task SendRawAsync(ClientWebSocket ws, GwEnvelope env, CancellationToken ct)
        {
            var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(env));
            await _sendLock.WaitAsync(ct);
            try { await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct); }
            catch { }
            finally { _sendLock.Release(); }
        }

        // convenience wrappers
        public void CreateRoom(Action<bool, string> done = null) => Request(Gw.PartyCreate, null, done);
        public void JoinRoom(string code, Action<bool, string> done = null) => Request(Gw.PartyJoin, new GwText { text = code }, done);
        public void LeaveRoom(Action<bool, string> done = null) => Request(Gw.PartyLeave, null, done);
        public void Invite(string friendId, Action<bool, string> done = null) => Request(Gw.PartyInvite, new GwId { id = friendId }, done);
        public void AcceptInvite(string inviteId, Action<bool, string> done = null) { Invites.RemoveAll(x => x.inviteId == inviteId); Request(Gw.InviteAccept, new GwId { id = inviteId }, done); }
        public void DeclineInvite(string inviteId) { Invites.RemoveAll(x => x.inviteId == inviteId); Request(Gw.InviteDecline, new GwId { id = inviteId }); Changed?.Invoke(); }
        public void SetReady(bool ready, Action<bool, string> done = null) => Request(Gw.PartyReady, new GwFlag { value = ready }, done);
        public void Kick(string id, Action<bool, string> done = null) => Request(Gw.PartyKick, new GwId { id = id }, done);
        public void Promote(string id, Action<bool, string> done = null) => Request(Gw.PartyPromote, new GwId { id = id }, done);
        public void StartQueue(int seconds, Action<bool, string> done = null) => Request(Gw.PartyStart, new GwStart { seconds = seconds }, done);
        public void CancelQueue(Action<bool, string> done = null) => Request(Gw.PartyCancel, null, done);
        public void SetLook(string look) => Request(Gw.PartyLook, new GwText { text = look });
        public void AddFriend(string handle, Action<bool, string> done = null) => Request(Gw.FriendRequest, new GwText { text = handle }, done);
        public void AcceptFriend(long requestId) => Request(Gw.FriendAccept, new GwRequestId { requestId = requestId });
        public void DeclineFriend(long requestId) => Request(Gw.FriendDecline, new GwRequestId { requestId = requestId });
        public void CancelFriend(long requestId) => Request(Gw.FriendCancel, new GwRequestId { requestId = requestId });
        public void RemoveFriend(string id) => Request(Gw.FriendRemove, new GwId { id = id });

        public PartyMemberInfo Me => Party.members.Find(m => m.id == MyId);
        public bool IsLeader => !Party.Empty && Party.leader == MyId;
    }
}
