using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Veil.Sim;

namespace Veil.Server
{
    /// <summary>One client WebSocket: a bounded outbound queue with a single writer, so hub pushes never block.</summary>
    public sealed class GatewayConnection
    {
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true };
        private readonly WebSocket _ws;
        private readonly Channel<string> _out = Channel.CreateBounded<string>(new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropOldest });
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        public SocialHub.Session Session;
        public string CloseReason = "";

        public GatewayConnection(WebSocket ws) { _ws = ws; }

        public void Send(GwEnvelope env) => _out.Writer.TryWrite(JsonSerializer.Serialize(env, Json));

        public void Close(string reason)
        {
            CloseReason = reason;
            Send(new GwEnvelope { t = Gw.Kicked, d = JsonSerializer.Serialize(new GwText { text = reason }, Json) });
            _out.Writer.TryComplete();
        }

        public async Task WriterLoop()
        {
            try
            {
                await foreach (var msg in _out.Reader.ReadAllAsync(_cts.Token))
                    await _ws.SendAsync(Encoding.UTF8.GetBytes(msg), WebSocketMessageType.Text, true, _cts.Token);
                // Open: we initiate the close. CloseReceived: the client asked to close — complete the handshake.
                if (_ws.State == WebSocketState.Open || _ws.State == WebSocketState.CloseReceived)
                    await _ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, CloseReason, CancellationToken.None);
            }
            catch { /* socket gone */ }
        }

        public void Complete() => _out.Writer.TryComplete();
        public void Stop() { _cts.Cancel(); _out.Writer.TryComplete(); }
    }

    /// <summary>
    /// <c>GET /ws</c> — the realtime channel for presence, friends, parties, invites and matchmaking.
    /// First frame must be <c>hello {id, token}</c> (the REST guest credentials). Silent connections close after 45 s.
    /// </summary>
    public static class Gateway
    {
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true };

        public static void Map(WebApplication app)
        {
            app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(15) });
            app.Map("/ws", async (HttpContext ctx, SocialHub hub, ILoggerFactory lf) =>
            {
                if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; await ctx.Response.WriteAsync("WebSocket only"); return; }
                using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
                var conn = new GatewayConnection(ws);
                var writer = Task.Run(conn.WriterLoop);
                var buf = new byte[16 * 1024];
                var sb = new StringBuilder();
                try
                {
                    while (ws.State == WebSocketState.Open)
                    {
                        sb.Clear();
                        WebSocketReceiveResult r;
                        using var idle = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                        do
                        {
                            r = await ws.ReceiveAsync(buf, idle.Token);
                            if (r.MessageType == WebSocketMessageType.Close) break;
                            sb.Append(Encoding.UTF8.GetString(buf, 0, r.Count));
                            if (sb.Length > 64 * 1024) throw new InvalidOperationException("frame too large");
                        } while (!r.EndOfMessage);
                        if (r.MessageType == WebSocketMessageType.Close) break;

                        var env = JsonSerializer.Deserialize<GwEnvelope>(sb.ToString(), Json);
                        if (env == null) continue;
                        if (conn.Session == null)
                        {
                            if (env.t != Gw.Hello) { conn.Close("say hello first"); break; }
                            var hello = string.IsNullOrEmpty(env.d) ? null : JsonSerializer.Deserialize<GwHello>(env.d, Json);
                            if (hello != null && hello.version != Gw.Version) { conn.Close("Update the game (gateway version mismatch)"); break; }
                            if (hub.Connect(conn, hello, out var err) == null) { conn.Close(err); break; }
                            continue;
                        }
                        hub.Handle(conn, env);
                    }
                }
                catch (OperationCanceledException) { /* idle timeout */ }
                catch (WebSocketException) { /* dropped */ }
                catch (Exception e) { lf.CreateLogger("Gateway").LogWarning("Gateway connection error: {Err}", e.Message); }
                finally
                {
                    hub.Disconnected(conn);
                    conn.Complete();                                     // flush queued frames (e.g. the kick reason)
                    await Task.WhenAny(writer, Task.Delay(1500));
                    conn.Stop();
                    await writer;
                }
            });
        }
    }
}
