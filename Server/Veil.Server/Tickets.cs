using System;
using System.Security.Cryptography;
using System.Text;

namespace Veil.Server
{
    /// <summary>What a match server needs to seat a player — nothing else is trusted from the client.</summary>
    public sealed class TicketData
    {
        public string ProfileId = "";
        public string Name = "";
        public int MatchId;
        public int Squad;
        public long ExpiresUnix;
    }

    /// <summary>
    /// HMAC-SHA256 signed tickets: <c>base64url(payload).base64url(mac)</c>. The Gateway issues them when a match is
    /// assigned; match servers (today in-process, later separate machines sharing the secret) validate them without a
    /// database call. The same ticket is used to reconnect until it expires.
    /// </summary>
    public sealed class TicketSigner
    {
        private readonly byte[] _key;

        public TicketSigner(string secret)
        {
            _key = string.IsNullOrEmpty(secret) ? RandomNumberGenerator.GetBytes(32) : SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        }

        public string Issue(TicketData t)
        {
            string payload = string.Join("|", Esc(t.ProfileId), Esc(t.Name), t.MatchId, t.Squad, t.ExpiresUnix);
            byte[] body = Encoding.UTF8.GetBytes(payload);
            return B64(body) + "." + B64(Mac(body));
        }

        public TicketData Validate(string ticket, out string error)
        {
            error = "";
            int dot = ticket?.IndexOf('.') ?? -1;
            if (dot <= 0) { error = "missing ticket"; return null; }
            byte[] body, mac;
            try { body = UnB64(ticket.Substring(0, dot)); mac = UnB64(ticket.Substring(dot + 1)); }
            catch { error = "malformed ticket"; return null; }
            if (!CryptographicOperations.FixedTimeEquals(mac, Mac(body))) { error = "bad signature"; return null; }
            var f = Encoding.UTF8.GetString(body).Split('|');
            if (f.Length != 5) { error = "malformed ticket"; return null; }
            var t = new TicketData { ProfileId = Unesc(f[0]), Name = Unesc(f[1]), MatchId = int.Parse(f[2]), Squad = int.Parse(f[3]), ExpiresUnix = long.Parse(f[4]) };
            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > t.ExpiresUnix) { error = "ticket expired"; return null; }
            return t;
        }

        /// <summary>Short keyed hash, used for voice channel tokens.</summary>
        public string Sign(string text) => B64(Mac(Encoding.UTF8.GetBytes(text))).Substring(0, 22);

        private byte[] Mac(byte[] data) { using var h = new HMACSHA256(_key); return h.ComputeHash(data); }
        private static string Esc(string s) => (s ?? "").Replace("|", "");
        private static string Unesc(string s) => s;
        private static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        private static byte[] UnB64(string s)
        {
            s = s.Replace('-', '+').Replace('_', '/');
            return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
        }
    }
}
