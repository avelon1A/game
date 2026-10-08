using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Veil.Net
{
    [Serializable] public sealed class ProfileDto
    {
        public string id; public string name; public int level; public int xp; public int xpToNext; public int rating;
        public int matches; public int wins; public int top3; public int bestScore; public int totalScore; public int avgScore;
        public int eliminations; public int deaths; public int objectives; public string appearance;
        public int coins; public string owned;
        public string heroXp; public string missions; public int missionDay;
    }
    [Serializable] public sealed class RegisterDto { public string id; public string token; public ProfileDto profile; }
    [Serializable] public sealed class LeaderboardDto { public ProfileDto[] players; }
    [Serializable] internal sealed class RegisterReq { public string name; }
    [Serializable] internal sealed class UpdateReq { public string token; public string name; public string appearance; }

    /// <summary>REST client for the backend (profiles, stats, leaderboard).</summary>
    public static class BackendApi
    {
        public static string BaseUrl(string host, int httpPort = 5080) => $"http://{host}:{httpPort}/api";

        public static IEnumerator Register(string baseUrl, string name, Action<RegisterDto> ok, Action<string> fail)
        {
            yield return Send("POST", baseUrl + "/players/register", JsonUtility.ToJson(new RegisterReq { name = name }), txt => ok(JsonUtility.FromJson<RegisterDto>(txt)), fail);
        }

        public static IEnumerator GetProfile(string baseUrl, string id, Action<ProfileDto> ok, Action<string> fail)
        {
            yield return Send("GET", baseUrl + "/players/" + UnityWebRequest.EscapeURL(id), null, txt => ok(JsonUtility.FromJson<ProfileDto>(txt)), fail);
        }

        public static IEnumerator UpdateProfile(string baseUrl, string id, string token, string name, string appearance, Action<ProfileDto> ok, Action<string> fail)
        {
            var body = JsonUtility.ToJson(new UpdateReq { token = token, name = name, appearance = appearance });
            yield return Send("PUT", baseUrl + "/players/" + UnityWebRequest.EscapeURL(id), body, txt => ok(JsonUtility.FromJson<ProfileDto>(txt)), fail);
        }

        public static IEnumerator Leaderboard(string baseUrl, Action<LeaderboardDto> ok, Action<string> fail)
        {
            yield return Send("GET", baseUrl + "/leaderboard?limit=10", null, txt => ok(JsonUtility.FromJson<LeaderboardDto>(txt)), fail);
        }

        private static IEnumerator Send(string method, string url, string json, Action<string> ok, Action<string> fail)
        {
            using var req = new UnityWebRequest(url, method);
            if (json != null)
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                req.SetRequestHeader("Content-Type", "application/json");
            }
            req.downloadHandler = new DownloadHandlerBuffer();
            req.timeout = 5;
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                fail?.Invoke($"{req.responseCode} {req.error}");
                yield break;
            }
            try { ok?.Invoke(req.downloadHandler.text); }
            catch (Exception e) { fail?.Invoke(e.Message); }
        }
    }
}
