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
        public string BackendId, BackendToken;

        // settings
        public float Sensitivity;
        public float Music, SfxVolume;
        public int MatchMinutes;
        public int Bots;
        public int Quality;   // 0 low, 1 high
        public bool Fullscreen;
        public int GyroMode;          // 0 off, 1 while firing, 2 always
        public float GyroSensitivity;
        public bool GyroInvertX, GyroInvertY;

        public static Profile Load()
        {
            var p = new Profile
            {
                Name = PlayerPrefs.GetString("name", "Player" + Random.Range(100, 999)),
                ServerHost = PlayerPrefs.GetString("host", "127.0.0.1"),
                ServerPort = PlayerPrefs.GetInt("port", 7777),
                HttpPort = PlayerPrefs.GetInt("http", 5080),
                BackendId = PlayerPrefs.GetString("bid", ""),
                BackendToken = PlayerPrefs.GetString("btok", ""),
                Sensitivity = PlayerPrefs.GetFloat("sens", 0.12f),
                Music = PlayerPrefs.GetFloat("music", 0.35f),
                SfxVolume = PlayerPrefs.GetFloat("sfx", 0.8f),
                MatchMinutes = PlayerPrefs.GetInt("minutes", 5),
                Bots = PlayerPrefs.GetInt("bots", 14),
                Quality = PlayerPrefs.GetInt("quality", 1),
                Fullscreen = PlayerPrefs.GetInt("fullscreen", 0) == 1,
                GyroMode = PlayerPrefs.GetInt("gyroMode", 1),
                GyroSensitivity = PlayerPrefs.GetFloat("gyroSens", 1.0f),
                GyroInvertX = PlayerPrefs.GetInt("gyroInvX", 0) == 1,
                GyroInvertY = PlayerPrefs.GetInt("gyroInvY", 0) == 1,
            };
            p.Look = new Appearance
            {
                Outfit = (byte)PlayerPrefs.GetInt("outfit", 0),
                Hair = (byte)PlayerPrefs.GetInt("hair", 0),
                HairColor = (byte)PlayerPrefs.GetInt("hairColor", 0),
                Accessory = (byte)PlayerPrefs.GetInt("accessory", 0),
                Color = (byte)PlayerPrefs.GetInt("color", 0),
            };
            return p;
        }

        public void Save()
        {
            PlayerPrefs.SetString("name", Name);
            PlayerPrefs.SetString("host", ServerHost);
            PlayerPrefs.SetInt("port", ServerPort);
            PlayerPrefs.SetInt("http", HttpPort);
            PlayerPrefs.SetString("bid", BackendId ?? "");
            PlayerPrefs.SetString("btok", BackendToken ?? "");
            PlayerPrefs.SetFloat("sens", Sensitivity);
            PlayerPrefs.SetFloat("music", Music);
            PlayerPrefs.SetFloat("sfx", SfxVolume);
            PlayerPrefs.SetInt("minutes", MatchMinutes);
            PlayerPrefs.SetInt("bots", Bots);
            PlayerPrefs.SetInt("quality", Quality);
            PlayerPrefs.SetInt("fullscreen", Fullscreen ? 1 : 0);
            PlayerPrefs.SetInt("gyroMode", GyroMode);
            PlayerPrefs.SetFloat("gyroSens", GyroSensitivity);
            PlayerPrefs.SetInt("gyroInvX", GyroInvertX ? 1 : 0);
            PlayerPrefs.SetInt("gyroInvY", GyroInvertY ? 1 : 0);
            PlayerPrefs.SetInt("outfit", Look.Outfit);
            PlayerPrefs.SetInt("hair", Look.Hair);
            PlayerPrefs.SetInt("hairColor", Look.HairColor);
            PlayerPrefs.SetInt("accessory", Look.Accessory);
            PlayerPrefs.SetInt("color", Look.Color);
            PlayerPrefs.Save();
        }

        public string AppearanceString => $"{Look.Outfit},{Look.Hair},{Look.HairColor},{Look.Accessory},{Look.Color}";
    }
}
