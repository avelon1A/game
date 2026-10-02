using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace Veil.App
{
    /// <summary>
    /// Remote "boot config": a small JSON file (config/boot.json in the public GitHub repo) that every installed
    /// app reads at startup. It says which server to use (so the server can move without a new APK), plus an
    /// announcement, maintenance mode and minimum / latest build for update prompts.
    /// Edit it with Tools/boot/boot.sh — never needs an app rebuild.
    /// </summary>
    [Serializable]
    public sealed class BootData
    {
        public int version;
        public string server = "";        // "udp://host:port", "host:port" or "host"
        public string message = "";       // lobby announcement (empty = none)
        public bool maintenance;          // true: online play paused, message explains why
        public int minBuild;              // builds below this must update (online disabled)
        public int latestBuild;           // builds below this get a "new version" notice
        public string updateUrl = "";     // where the UPDATE button goes (APK / store page)
        public string googleClientId = ""; // OAuth *Web* client id for Sign in with Google (server checks it as audience)
    }

    public static class BootConfig
    {
        /// <summary>This app's build number. Bump it for every APK you hand out (it is also the Android versionCode).</summary>
        public const int Build = 3;

        // tried in order; the second is a CDN mirror of the same file in case GitHub raw is blocked on a network
        private static readonly string[] Urls =
        {
            "https://raw.githubusercontent.com/avelon1A/game/main/config/boot.json",
            "https://cdn.jsdelivr.net/gh/avelon1A/game@main/config/boot.json",
        };

        private const string CacheKey = "bootCache";

        /// <summary>The newest config known (fresh from the network, else the last one cached on this device).</summary>
        public static BootData Current { get; private set; }

        public static bool UpdateRequired => Current != null && Current.minBuild > Build;
        public static bool UpdateAvailable => Current != null && Current.latestBuild > Build;
        public static bool Maintenance => Current != null && Current.maintenance;

        /// <summary>The last config this device downloaded (works offline / when GitHub is unreachable).</summary>
        public static BootData LoadCached()
        {
            var d = Parse(PlayerPrefs.GetString(CacheKey, ""));
            if (d != null && Current == null) Current = d;
            return d;
        }

        public static IEnumerator Fetch(Action<BootData> done)
        {
            // minute-granular cache buster: edits show up within ~a minute instead of the CDN's 5 minutes
            string bust = "?t=" + (DateTime.UtcNow.Ticks / TimeSpan.TicksPerMinute);
            var urls = Urls;
            var args = Environment.GetCommandLineArgs();   // testing: -bootconfig file:///path/boot.json
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "-bootconfig") { urls = new[] { args[i + 1] }; bust = ""; }
            foreach (var url in urls)
            {
                using (var req = UnityWebRequest.Get(url + bust))
                {
                    req.timeout = 6;
                    yield return req.SendWebRequest();
                    if (req.result != UnityWebRequest.Result.Success) { Debug.Log($"[VEIL] boot config {url}: {req.error}"); continue; }
                    var d = Parse(req.downloadHandler.text);
                    if (d == null) { Debug.LogWarning($"[VEIL] boot config {url}: bad JSON"); continue; }
                    PlayerPrefs.SetString(CacheKey, req.downloadHandler.text);
                    PlayerPrefs.Save();
                    Current = d;
                    Debug.Log($"[VEIL] boot config: server {d.server}, build {Build} (min {d.minBuild}, latest {d.latestBuild})");
                    done?.Invoke(d);
                    yield break;
                }
            }
            done?.Invoke(null);
        }

        private static BootData Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                var d = JsonUtility.FromJson<BootData>(json);
                return d != null && d.version >= 1 ? d : null;
            }
            catch (Exception) { return null; }
        }
    }
}
