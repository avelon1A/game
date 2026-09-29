using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Veil.Server
{
    public sealed class RegisterRequest { public string name { get; set; } }
    public sealed class ProfileUpdate { public string token { get; set; } public string name { get; set; } public string appearance { get; set; } }

    /// <summary>REST backend: guest profiles, progression, leaderboard, match history, server list.</summary>
    public static class Api
    {
        public static void Map(WebApplication app)
        {
            app.MapGet("/", () => Results.Text(
                "VEIL test backend\n\nGET  /api/health\nPOST /api/players/register {name}\nGET  /api/players/{id}\nPUT  /api/players/{id} {token,name,appearance}\n" +
                "GET  /api/leaderboard\nGET  /api/matches/recent\nGET  /api/servers\n\nWS   /ws  (Gateway: presence, friends, parties, matchmaking)\n"));

            var api = app.MapGroup("/api");

            api.MapGet("/health", (GameHost gs, SocialHub hub, VoiceRelay voice) => Results.Ok(new
            {
                ok = true, time = DateTime.UtcNow, server = gs.Describe(),
                social = new { online = hub.OnlineCount, parties = hub.PartyCount, queued = hub.QueuedParties },
                voice = new { speakers = voice.Speakers, framesForwarded = voice.FramesForwarded },
            }));

            api.MapPost("/players/register", (RegisterRequest req, Database db) =>
            {
                var (id, token) = db.Register(req?.name);
                return Results.Ok(new { id, token, profile = db.Get(id) });
            });

            api.MapGet("/players/{id}", (string id, Database db) =>
            {
                var p = db.Get(id);
                return p == null ? Results.NotFound(new { error = "unknown player" }) : Results.Ok(p);
            });

            api.MapPut("/players/{id}", (string id, ProfileUpdate req, Database db) =>
            {
                if (req == null || !db.CheckToken(id, req.token)) return Results.Unauthorized();
                db.UpdateProfile(id, req.name, req.appearance);
                return Results.Ok(db.Get(id));
            });

            api.MapGet("/leaderboard", (Database db, int? limit) => Results.Ok(new { players = db.Leaderboard(Math.Clamp(limit ?? 20, 1, 100)) }));

            api.MapGet("/matches/recent", (Database db, int? limit) => Results.Ok(new { matches = db.RecentMatches(Math.Clamp(limit ?? 10, 1, 50)) }));

            // Server list for a future multi-region fleet; one entry today.
            api.MapGet("/servers", (GameHost gs) => Results.Ok(new { servers = new[] { gs.Describe() } }));
        }
    }
}
