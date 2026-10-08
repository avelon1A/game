using StringComparison = System.StringComparison;
using UnityEngine;
using Veil.Sim;

namespace Veil.App
{
    /// <summary>Local player profile + settings (PlayerPrefs). Cosmetic appearance only — no gameplay power.</summary>
    public sealed class Profile
    {
        public string Name;
        public Appearance Look;
        public string ServerHost;
        public int ServerPort;
        public int HttpPort;
        /// <summary>Gateway over UDP (address written as udp://host:port) — for UDP-only tunnels such as playit.gg.</summary>
        public bool ServerUdp;
        /// <summary>Server typed by the player (Settings / lobby field). Empty = automatic.</summary>
        public string ServerOverride = "";
        /// <summary>Automatic server: from the remote boot config (cached), else the one built into the app.</summary>
        public string AutoServer = "";
        public string EffectiveServer => !string.IsNullOrEmpty(ServerOverride) ? ServerOverride : AutoServer;
        /// <summary>Lobby mode: online squads (default) or practice vs bots.</summary>
        public bool LobbyOnline;
        public string BackendId, BackendToken;

        // settings
        public float Sensitivity;
        public float Music, SfxVolume;
        public bool MusicOn = true;
        public int MatchMinutes;
        public int Bots;
        public int Quality;   // 0 low, 1 high
        public bool Fullscreen;
        public int GyroMode;          // 0 off, 1 while firing, 2 always
        public float CamDistance;     // third-person camera distance (m)
        public float GyroSensitivity;
        public bool GyroInvertX, GyroInvertY;
        public int VoiceMode;              // 0 push-to-talk, 1 open mic, 2 off
        public float VoiceVolume, MicSensitivity;
        public string Handle;              // Name#1234 from the Gateway
        public string GoogleEmail = "";    // Google account linked to this player ("" = guest)

        public static Profile Load()
        {
            var p = new Profile
            {
                Name = PlayerPrefs.GetString("name", "Player" + Random.Range(100, 999)),
                ServerHost = "127.0.0.1",
                ServerPort = PlayerPrefs.GetInt("port", 7777),
                HttpPort = 5080,
                BackendId = PlayerPrefs.GetString("bid", ""),
                BackendToken = PlayerPrefs.GetString("btok", ""),
                Sensitivity = PlayerPrefs.GetFloat("sens", 0.12f),
                Music = PlayerPrefs.GetFloat("music", 0.35f),
                MusicOn = PlayerPrefs.GetInt("musicOn", 1) == 1,
                SfxVolume = PlayerPrefs.GetFloat("sfx", 0.8f),
                MatchMinutes = PlayerPrefs.GetInt("minutes", 5),
                Bots = PlayerPrefs.GetInt("bots", 14),
                Quality = PlayerPrefs.GetInt("quality", 1),
                Fullscreen = PlayerPrefs.GetInt("fullscreen", 0) == 1,
                GyroMode = PlayerPrefs.GetInt("gyroMode", 1),
                CamDistance = PlayerPrefs.GetFloat("camDist", Application.isMobilePlatform ? 3.4f : 7.5f),
                GyroSensitivity = PlayerPrefs.GetFloat("gyroSens", 1.0f),
                GyroInvertX = PlayerPrefs.GetInt("gyroInvX", 0) == 1,
                GyroInvertY = PlayerPrefs.GetInt("gyroInvY", 0) == 1,
                VoiceMode = PlayerPrefs.GetInt("voiceMode", 0),
                LobbyOnline = PlayerPrefs.GetInt("lobbyOnline", 1) == 1,
                VoiceVolume = PlayerPrefs.GetFloat("voiceVol", 1f),
                MicSensitivity = PlayerPrefs.GetFloat("micSens", 0.35f),
                Handle = PlayerPrefs.GetString("handle", ""),
                GoogleEmail = PlayerPrefs.GetString("googleEmail", ""),
            };
            // server: what the player typed, else the remote boot config (last cached copy), else the built-in default
            var defAsset = Resources.Load<TextAsset>("server_default");
            string baked = defAsset != null ? defAsset.text.Trim() : "";
            var boot = BootConfig.LoadCached();
            p.AutoServer = boot != null && !string.IsNullOrEmpty(boot.server) ? boot.server.Trim() : baked;
            p.ServerOverride = PlayerPrefs.GetString("serverOverride", "");
            p.ParseAddress(p.EffectiveServer);
            p.MatchMinutes = 10;   // no length choice any more: 10 min is only the pacing plan, matches run until a squad escapes
            p.Look = new Appearance
            {
                Outfit = (byte)PlayerPrefs.GetInt("outfit", 0),
                Hair = (byte)PlayerPrefs.GetInt("hair", 0),
                HairColor = (byte)PlayerPrefs.GetInt("hairColor", 0),
                Accessory = (byte)PlayerPrefs.GetInt("accessory", 0),
                Color = (byte)PlayerPrefs.GetInt("color", 0),
                Weapon = (byte)Mathf.Min(PlayerPrefs.GetInt("weapon", 0), 1),   // 0 rifle, 1 sniper (fists = in-match switch)
            };
            return p;
        }

        public void Save()
        {
            PlayerPrefs.SetString("name", Name);
            PlayerPrefs.SetInt("port", ServerPort);
            PlayerPrefs.SetString("bid", BackendId ?? "");
            PlayerPrefs.SetString("btok", BackendToken ?? "");
            PlayerPrefs.SetFloat("sens", Sensitivity);
            PlayerPrefs.SetFloat("music", Music);
            PlayerPrefs.SetFloat("camDist", CamDistance);
            PlayerPrefs.SetInt("musicOn", MusicOn ? 1 : 0);
            PlayerPrefs.SetFloat("sfx", SfxVolume);
            PlayerPrefs.SetInt("minutes", MatchMinutes);
            PlayerPrefs.SetInt("bots", Bots);
            PlayerPrefs.SetInt("quality", Quality);
            PlayerPrefs.SetInt("fullscreen", Fullscreen ? 1 : 0);
            PlayerPrefs.SetInt("gyroMode", GyroMode);
            PlayerPrefs.SetFloat("gyroSens", GyroSensitivity);
            PlayerPrefs.SetInt("gyroInvX", GyroInvertX ? 1 : 0);
            PlayerPrefs.SetInt("gyroInvY", GyroInvertY ? 1 : 0);
            PlayerPrefs.SetInt("voiceMode", VoiceMode);
            PlayerPrefs.SetInt("lobbyOnline", LobbyOnline ? 1 : 0);
            PlayerPrefs.SetFloat("voiceVol", VoiceVolume);
            PlayerPrefs.SetFloat("micSens", MicSensitivity);
            PlayerPrefs.SetString("handle", Handle ?? "");
            PlayerPrefs.SetString("googleEmail", GoogleEmail ?? "");
            PlayerPrefs.SetInt("outfit", Look.Outfit);
            PlayerPrefs.SetInt("hair", Look.Hair);
            PlayerPrefs.SetInt("hairColor", Look.HairColor);
            PlayerPrefs.SetInt("accessory", Look.Accessory);
            PlayerPrefs.SetInt("color", Look.Color);
            PlayerPrefs.SetInt("weapon", Look.Weapon);
            PlayerPrefs.Save();
        }

        /// <summary>"host" or "host:port" (port = the web/Gateway port, e.g. a playit.gg TCP tunnel).</summary>
        /// <summary>Player-typed server; empty or "auto" returns to the automatic (boot config) server.</summary>
        public void SetServerAddress(string s)
        {
            s = (s ?? "").Trim();
            ServerOverride = s.Equals("auto", StringComparison.OrdinalIgnoreCase) ? "" : s;
            PlayerPrefs.SetString("serverOverride", ServerOverride);
            PlayerPrefs.Save();
            ParseAddress(EffectiveServer);
        }

        /// <summary>Use a server for this run only (tests); nothing is saved.</summary>
        public void UseServerForSession(string s) => ParseAddress(s);

        /// <summary>New automatic server from the boot config. True if the server actually in use changed.</summary>
        public bool SetAutoServer(string s)
        {
            s = (s ?? "").Trim();
            if (string.IsNullOrEmpty(s) || s == AutoServer) return false;
            AutoServer = s;
            if (!string.IsNullOrEmpty(ServerOverride)) return false;
            string before = ServerAddress;
            ParseAddress(AutoServer);
            return ServerAddress != before;
        }

        /// <summary>"host", "host:port" (web/Gateway port) or "udp://host:port" (Gateway over UDP).</summary>
        public void ParseAddress(string s)
        {
            s = (s ?? "").Trim();
            ServerUdp = s.StartsWith("udp://");
            if (ServerUdp) s = s.Substring(6);
            if (s.StartsWith("http://")) s = s.Substring(7);
            if (s.StartsWith("https://")) s = s.Substring(8);
            s = s.TrimEnd('/');
            int i = s.LastIndexOf(':');
            if (i > 0 && s.IndexOf(':') == i && int.TryParse(s.Substring(i + 1), out int port)) { ServerHost = s.Substring(0, i); HttpPort = port; }
            else { ServerHost = s; HttpPort = ServerUdp ? Veil.Sim.Gw.UdpPort : 5080; }
            if (string.IsNullOrEmpty(ServerHost)) { ServerHost = "127.0.0.1"; ServerUdp = false; HttpPort = 5080; }
        }

        public string ServerAddress => ServerUdp ? $"udp://{ServerHost}:{HttpPort}" : HttpPort == 5080 ? ServerHost : $"{ServerHost}:{HttpPort}";

        public string AppearanceString => $"{Look.Outfit},{Look.Hair},{Look.HairColor},{Look.Accessory},{Look.Color},{Look.Weapon}";
    }
}
