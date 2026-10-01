using System;
using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Veil.Server
{
    public sealed class ServerOptions
    {
        public int UdpPort = 7777;
        public int HttpPort = 5080;
        public int VoicePort = 7778;
        public int GatewayUdpPort = Veil.Sim.Gw.UdpPort;   // Gateway over UDP (for UDP-only tunnels)
        public string Name = "VEIL Test Server";
        public string DbPath = "veil.db";
        /// <summary>Seconds the oldest queued party waits for more players before bots fill the match.</summary>
        public double MatchmakingWait = 6;
        /// <summary>Shared by the Gateway (issues tickets) and match hosts (validate them). Env VEIL_TICKET_SECRET.</summary>
        public string TicketSecret = Environment.GetEnvironmentVariable("VEIL_TICKET_SECRET") ?? "";
        /// <summary>Public addresses when behind a tunnel / NAT (e.g. playit.gg gives each port its own host:port).
        /// Empty host = clients reuse the Gateway host; 0 port = the local port.</summary>
        public string PublicMatchHost = "", PublicVoiceHost = "";
        /// <summary>Google OAuth *Web* client id(s) — the audience of ID tokens from the app (comma-separated).</summary>
        public string[] GoogleClientIds = (Environment.GetEnvironmentVariable("VEIL_GOOGLE_CLIENT_ID") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        public int PublicMatchPort, PublicVoicePort;
    }

    public static class Program
    {
        private static (string, int) HostPort(string s)
        {
            int i = s.LastIndexOf(':');
            return i > 0 ? (s.Substring(0, i), int.Parse(s.Substring(i + 1))) : (s, 0);
        }

        public static int Main(string[] args)
        {
            var opt = new ServerOptions();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string Next() => i + 1 < args.Length ? args[++i] : "";
                switch (a)
                {
                    case "--selftest":
                    {
                        int secs = i + 1 < args.Length && int.TryParse(args[i + 1], out var s) ? int.Parse(Next()) : 300;
                        int n = i + 1 < args.Length && int.TryParse(args[i + 1], out var c) ? int.Parse(Next()) : 1;
                        return SelfTest.Run(secs, n);
                    }
                    case "--udp": opt.UdpPort = int.Parse(Next()); break;
                    case "--http": opt.HttpPort = int.Parse(Next()); break;
                    case "--voice": opt.VoicePort = int.Parse(Next()); break;
                    case "--gateway-udp": opt.GatewayUdpPort = int.Parse(Next()); break;
                    case "--mm-wait": opt.MatchmakingWait = double.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
                    case "--name": opt.Name = Next(); break;
                    case "--public-match": (opt.PublicMatchHost, opt.PublicMatchPort) = HostPort(Next()); break;
                    case "--public-voice": (opt.PublicVoiceHost, opt.PublicVoicePort) = HostPort(Next()); break;
                    case "--db": opt.DbPath = Next(); break;
                    case "--google-client-id": opt.GoogleClientIds = Next().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries); break;
                }
            }

            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.Logging.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; });
            builder.Logging.SetMinimumLevel(LogLevel.Information);
            builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
            builder.WebHost.UseUrls($"http://0.0.0.0:{opt.HttpPort}");

            var db = new Database(Path.GetFullPath(opt.DbPath));
            builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.IncludeFields = true);
            builder.Services.AddSingleton(opt);
            builder.Services.AddSingleton(db);
            builder.Services.AddSingleton(new TicketSigner(opt.TicketSecret));
            builder.Services.AddSingleton<GameHost>();
            builder.Services.AddSingleton<SocialHub>();
            builder.Services.AddSingleton<VoiceRelay>();
            builder.Services.AddSingleton<UdpGateway>();
            builder.Services.AddSingleton<AdminAuth>();
            builder.Services.AddSingleton<AdminMetrics>();
            builder.Services.AddHostedService(sp => sp.GetRequiredService<GameHost>());
            builder.Services.AddHostedService(sp => sp.GetRequiredService<SocialHub>());
            builder.Services.AddHostedService(sp => sp.GetRequiredService<VoiceRelay>());
            builder.Services.AddHostedService(sp => sp.GetRequiredService<UdpGateway>());
            builder.Services.AddHostedService(sp => sp.GetRequiredService<AdminMetrics>());

            var app = builder.Build();
            Gateway.Map(app);
            Api.Map(app);
            AdminApi.Map(app);

            Console.WriteLine($"VEIL server  |  REST http://localhost:{opt.HttpPort}/api/health  |  Gateway ws://localhost:{opt.HttpPort}/ws  |  " +
                              $"UDP gateway {opt.GatewayUdpPort}  |  UDP match {opt.UdpPort}  |  UDP voice {opt.VoicePort}  |  db {db.Path}");
            app.Run();
            return 0;
        }
    }
}
