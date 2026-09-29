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
        public string Name = "VEIL Test Server";
        public string DbPath = "veil.db";
        /// <summary>Seconds the oldest queued party waits for more players before bots fill the match.</summary>
        public double MatchmakingWait = 6;
        /// <summary>Shared by the Gateway (issues tickets) and match hosts (validate them). Env VEIL_TICKET_SECRET.</summary>
        public string TicketSecret = Environment.GetEnvironmentVariable("VEIL_TICKET_SECRET") ?? "";
    }

    public static class Program
    {
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
                    case "--mm-wait": opt.MatchmakingWait = double.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
                    case "--name": opt.Name = Next(); break;
                    case "--db": opt.DbPath = Next(); break;
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
            builder.Services.AddHostedService(sp => sp.GetRequiredService<GameHost>());
            builder.Services.AddHostedService(sp => sp.GetRequiredService<SocialHub>());
            builder.Services.AddHostedService(sp => sp.GetRequiredService<VoiceRelay>());

            var app = builder.Build();
            Gateway.Map(app);
            Api.Map(app);

            Console.WriteLine($"VEIL server  |  REST http://localhost:{opt.HttpPort}/api/health  |  Gateway ws://localhost:{opt.HttpPort}/ws  |  " +
                              $"UDP match {opt.UdpPort}  |  UDP voice {opt.VoicePort}  |  db {db.Path}");
            app.Run();
            return 0;
        }
    }
}
