using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Veil.Server
{
    /// <summary>
    /// Head-user access to the admin dashboard. Keys live in a plain file (one "name:key" per line, default
    /// /opt/rilo/admins.txt or $VEIL_ADMINS_FILE) that is re-read when it changes — add or remove admins without a restart.
    /// </summary>
    public sealed class AdminAuth
    {
        private readonly string _path;
        private readonly ILogger<AdminAuth> _log;
        private DateTime _stamp;
        private Dictionary<string, string> _keys = new Dictionary<string, string>();   // key -> admin name
        private readonly ConcurrentDictionary<string, (int fails, DateTime until)> _lockout = new ConcurrentDictionary<string, (int, DateTime)>();

        public AdminAuth(ILogger<AdminAuth> log)
        {
            _log = log;
            _path = Environment.GetEnvironmentVariable("VEIL_ADMINS_FILE") ?? (Directory.Exists("/opt/rilo") ? "/opt/rilo/admins.txt" : "admins.txt");
        }

        private void Reload()
        {
            try
            {
                var t = File.Exists(_path) ? File.GetLastWriteTimeUtc(_path) : DateTime.MinValue;
                if (t == _stamp) return;
                _stamp = t;
                var keys = new Dictionary<string, string>();
                if (File.Exists(_path))
                    foreach (var line in File.ReadAllLines(_path))
                    {
                        var l = line.Trim();
                        if (l.Length == 0 || l.StartsWith("#")) continue;
                        int i = l.IndexOf(':');
                        if (i > 0 && l.Length - i > 16) keys[l.Substring(i + 1).Trim()] = l.Substring(0, i).Trim();
                    }
                _keys = keys;
                _log.LogInformation("Admin keys loaded: {Count}", keys.Count);
            }
            catch (Exception e) { _log.LogWarning("Admin keys: {Err}", e.Message); }
        }

        /// <summary>Admin name for a valid key, else null. Repeated failures from one address are locked out for 10 minutes.</summary>
        public string Check(HttpContext ctx)
        {
            string ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "?";
            if (_lockout.TryGetValue(ip, out var lo) && lo.fails >= 8 && DateTime.UtcNow < lo.until) return null;
            Reload();
            string key = ctx.Request.Headers["X-Admin-Key"].ToString();
            foreach (var kv in _keys)
                if (key.Length == kv.Key.Length && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(kv.Key)))
                {
                    _lockout.TryRemove(ip, out _);
                    return kv.Value;
                }
            var f = _lockout.TryGetValue(ip, out var o) && DateTime.UtcNow < o.until ? o.fails + 1 : 1;
            _lockout[ip] = (f, DateTime.UtcNow.AddMinutes(10));
            if (key.Length > 0) _log.LogWarning("Admin key rejected from {Ip}", ip);
            return null;
        }
    }

    /// <summary>Samples server activity every 30 s (24 h ring) for the dashboard charts.</summary>
    public sealed class AdminMetrics : BackgroundService
    {
        public sealed class Sample { public long t; public int online, inMatch, queued, matches; public double tickMs, memMb, cpu; }

        private readonly GameHost _host;
        private readonly SocialHub _hub;
        private readonly LinkedList<Sample> _ring = new LinkedList<Sample>();
        private readonly object _lock = new object();
        public readonly DateTime StartedUtc = DateTime.UtcNow;
        public Sample Last { get; private set; } = new Sample();

        public AdminMetrics(GameHost host, SocialHub hub) { _host = host; _hub = hub; }

        protected override async Task ExecuteAsync(CancellationToken stop)
        {
            var proc = Process.GetCurrentProcess();
            var lastCpu = proc.TotalProcessorTime; var lastWall = DateTime.UtcNow;
            while (!stop.IsCancellationRequested)
            {
                proc.Refresh();
                var now = DateTime.UtcNow;
                double cpu = (proc.TotalProcessorTime - lastCpu).TotalMilliseconds / Math.Max(1, (now - lastWall).TotalMilliseconds) / Environment.ProcessorCount * 100;
                lastCpu = proc.TotalProcessorTime; lastWall = now;
                var s = new Sample
                {
                    t = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), online = _hub.OnlineCount, inMatch = _host.PublicPlayers, queued = _hub.QueuedParties,
                    matches = _host.PublicMatches, tickMs = Math.Round(_host.LastTickMs, 3), memMb = Math.Round(proc.WorkingSet64 / 1048576.0, 1), cpu = Math.Round(cpu, 1),
                };
                lock (_lock) { _ring.AddLast(s); while (_ring.Count > 2880) _ring.RemoveFirst(); }
                Last = s;
                try { await Task.Delay(TimeSpan.FromSeconds(30), stop); } catch (TaskCanceledException) { }
            }
        }

        public Sample[] History() { lock (_lock) return _ring.ToArray(); }
    }

    public static class AdminApi
    {
        private static string _page;

        public static void Map(WebApplication app)
        {
            // the dashboard itself (public HTML; every data call below needs an admin key)
            app.MapGet("/admin", () =>
            {
                if (_page == null)
                {
                    using var st = Assembly.GetExecutingAssembly().GetManifestResourceStream("Veil.Server.admin.html");
                    _page = st == null ? "admin.html missing" : new StreamReader(st).ReadToEnd();
                }
                return Results.Content(_page, "text/html; charset=utf-8");
            });

            var g = app.MapGroup("/api/admin");
            g.AddEndpointFilter(async (ctx, next) =>
            {
                var auth = ctx.HttpContext.RequestServices.GetService(typeof(AdminAuth)) as AdminAuth;
                var who = auth?.Check(ctx.HttpContext);
                if (who == null) return Results.Json(new { error = "admin key required" }, statusCode: 401);
                ctx.HttpContext.Items["admin"] = who;
                ctx.HttpContext.Response.Headers["Cache-Control"] = "no-store";
                return await next(ctx);
            });

            g.MapGet("/overview", (HttpContext http, GameHost gs, SocialHub hub, VoiceRelay voice, Database db, AdminMetrics m) => Results.Ok(new
            {
                admin = http.Items["admin"],
                time = DateTime.UtcNow,
                server = new
                {
                    name = gs.Options.Name, version = Veil.Sim.Gw.Version, uptimeMinutes = (int)(DateTime.UtcNow - m.StartedUtc).TotalMinutes,
                    tickMs = Math.Round(gs.LastTickMs, 3), memMb = m.Last.memMb, cpu = m.Last.cpu, cores = Environment.ProcessorCount,
                    matchesPlayed = Interlocked.Read(ref gs.MatchesPlayed), voiceSpeakers = voice.Speakers, voiceFrames = voice.FramesForwarded,
                },
                live = new { online = hub.OnlineCount, parties = hub.PartyCount, queued = hub.QueuedParties, matches = gs.PublicMatches, inMatch = gs.PublicPlayers },
                social = hub.AdminSnapshot(),
                matches = gs.LiveMatches,
                totals = db.AdminStats(),
                recent = db.RecentMatches(15),
            }));

            g.MapGet("/players", (Database db, string q, int? limit) => Results.Ok(new { players = db.AdminPlayers(q ?? "", Math.Clamp(limit ?? 100, 1, 500)) }));
            g.MapGet("/history", (AdminMetrics m) => Results.Ok(new { samples = m.History() }));
        }
    }
}
