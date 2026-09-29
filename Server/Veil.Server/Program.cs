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
        public int MatchSeconds = 300;
        public int TotalPlayers = 15;
        public int MinHumans = 1;
        public float LobbyCountdown = 5f;
        public string Name = "VEIL Test Server";
        public string DbPath = "veil.db";
        public string PublicHost = "127.0.0.1";
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
                    case "--match-seconds": opt.MatchSeconds = int.Parse(Next()); break;
                    case "--players": opt.TotalPlayers = Math.Clamp(int.Parse(Next()), 1, 15); break;
                    case "--name": opt.Name = Next(); break;
                    case "--db": opt.DbPath = Next(); break;
                    case "--host": opt.PublicHost = Next(); break;
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
            builder.Services.AddSingleton<GameServer>();
            builder.Services.AddHostedService(sp => sp.GetRequiredService<GameServer>());

            var app = builder.Build();
            Api.Map(app);

            Console.WriteLine($"VEIL server  |  REST http://localhost:{opt.HttpPort}/api/health  |  UDP game port {opt.UdpPort}  |  db {db.Path}");
            app.Run();
            return 0;
        }
    }
}
