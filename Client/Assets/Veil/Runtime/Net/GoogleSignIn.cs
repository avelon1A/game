using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Veil.Net
{
    /// <summary>
    /// "Sign in with Google". Android: Credential Manager account picker (Plugins/Android/RiloGoogleSignIn.java).
    /// iOS: Google's OAuth page in Apple's ASWebAuthenticationSession (Plugins/iOS/RiloGoogleSignIn.mm), authorization
    /// code + PKCE, exchanged here for an ID token issued to the iOS client. Either way the Rilo server verifies the token.
    /// </summary>
    public static class GoogleSignIn
    {
        public struct Result
        {
            public bool Ok;
            public string IdToken, Email, Name, Error;
            public bool Cancelled => Error == "cancelled";
        }

        public static bool Supported => Application.platform == RuntimePlatform.Android || Application.platform == RuntimePlatform.IPhonePlayer;

        private static readonly ConcurrentQueue<Action> Main = new ConcurrentQueue<Action>();

        /// <summary>Call every frame (results arrive on a Java thread).</summary>
        public static void Pump() { while (Main.TryDequeue(out var a)) a(); }

        /// <param name="webClientId">the OAuth *Web* client id (audience the server checks)</param>
        public static void SignIn(string webClientId, Action<Result> done)
        {
            if (!Supported) { done(new Result { Error = "Google sign-in is available in the phone app" }); return; }
            if (Application.platform == RuntimePlatform.IPhonePlayer) { IosReceiver.Begin(done); return; }
            if (string.IsNullOrEmpty(webClientId)) { done(new Result { Error = "Google sign-in isn't configured yet" }); return; }
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var cls = new AndroidJavaClass("com.veilstudio.rilo.RiloGoogleSignIn");
                cls.CallStatic("signIn", activity, webClientId, new Listener(done));
            }
            catch (Exception e) { done(new Result { Error = e.Message }); }
        }

        public static void SignOut()
        {
            if (Application.platform != RuntimePlatform.Android) return;
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var cls = new AndroidJavaClass("com.veilstudio.rilo.RiloGoogleSignIn");
                cls.CallStatic("signOut", activity);
            }
            catch (Exception e) { Debug.LogWarning("[VEIL] Google sign-out: " + e.Message); }
        }

        private sealed class Listener : AndroidJavaProxy
        {
            private readonly Action<Result> _done;
            public Listener(Action<Result> done) : base("com.veilstudio.rilo.RiloGoogleSignIn$Listener") { _done = done; }

            // called by Java (background thread) → hand over to Unity's main thread
            public void onResult(bool ok, string idToken, string email, string name, string error)
            {
                var r = new Result { Ok = ok, IdToken = idToken, Email = email, Name = name, Error = error };
                Main.Enqueue(() => _done(r));
            }
        }
    
        // ------------------------------------------------------------------ iOS

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void RiloGoogleAuth(string url, string scheme, string receiver);
#else
        private static void RiloGoogleAuth(string url, string scheme, string receiver) { }
#endif

        private static string B64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        [Serializable] private sealed class TokenReply { public string id_token; public string error; public string error_description; }

        /// <summary>Receives the callback URL from the native side (UnitySendMessage) and swaps the code for an ID token.</summary>
        private sealed class IosReceiver : MonoBehaviour
        {
            private static IosReceiver _inst;
            private Action<Result> _done;
            private string _verifier, _state, _clientId, _redirect;

            public static void Begin(Action<Result> done)
            {
                var res = Resources.Load<TextAsset>("google_ios_client_id");
                string id = res != null ? res.text.Trim() : "";
                if (id == "") { done(new Result { Error = "Google sign-in isn't configured for iPhone yet" }); return; }
                if (_inst == null) { var go = new GameObject("RiloGoogleIOS"); DontDestroyOnLoad(go); _inst = go.AddComponent<IosReceiver>(); }
                _inst.Launch(id, done);
            }

            private void Launch(string clientId, Action<Result> done)
            {
                _done = done; _clientId = clientId;
                var rnd = new byte[32]; using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(rnd);
                _verifier = B64Url(rnd);
                var st = new byte[16]; using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(st);
                _state = B64Url(st);
                string challenge; using (var sha = SHA256.Create()) challenge = B64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(_verifier)));
                // iOS clients use the reversed client id as their URL scheme
                string scheme = "com.googleusercontent.apps." + clientId.Replace(".apps.googleusercontent.com", "");
                _redirect = scheme + ":/oauth2redirect";
                string url = "https://accounts.google.com/o/oauth2/v2/auth?client_id=" + Uri.EscapeDataString(clientId)
                    + "&redirect_uri=" + Uri.EscapeDataString(_redirect) + "&response_type=code&scope=" + Uri.EscapeDataString("openid email profile")
                    + "&code_challenge=" + challenge + "&code_challenge_method=S256&state=" + _state + "&prompt=select_account";
                RiloGoogleAuth(url, scheme, gameObject.name);
            }

            private void Finish(Result r) { var d = _done; _done = null; d?.Invoke(r); }

            // called by RiloGoogleSignIn.mm
            public void OnGoogleAuth(string msg)
            {
                if (msg.StartsWith("error:")) { Finish(new Result { Error = msg.Substring(6) }); return; }
                string code = null, state = null, err = null;
                int q = msg.IndexOf('?');
                if (q >= 0)
                    foreach (var kv in msg.Substring(q + 1).Split('&'))
                    {
                        int e = kv.IndexOf('='); if (e < 0) continue;
                        string k = kv.Substring(0, e), v = Uri.UnescapeDataString(kv.Substring(e + 1));
                        if (k == "code") code = v; else if (k == "state") state = v; else if (k == "error") err = v;
                    }
                if (err != null) { Finish(new Result { Error = err == "access_denied" ? "cancelled" : err }); return; }
                if (code == null || state != _state) { Finish(new Result { Error = "invalid sign-in response" }); return; }
                StartCoroutine(Exchange(code));
            }

            private IEnumerator Exchange(string code)
            {
                var form = new WWWForm();
                form.AddField("code", code);
                form.AddField("client_id", _clientId);
                form.AddField("redirect_uri", _redirect);
                form.AddField("grant_type", "authorization_code");
                form.AddField("code_verifier", _verifier);
                using var req = UnityWebRequest.Post("https://oauth2.googleapis.com/token", form);
                yield return req.SendWebRequest();
                TokenReply t = null;
                try { t = JsonUtility.FromJson<TokenReply>(req.downloadHandler.text); } catch { }
                if (t == null || string.IsNullOrEmpty(t.id_token))
                    Finish(new Result { Error = t?.error_description ?? t?.error ?? req.error ?? "no token" });
                else Finish(new Result { Ok = true, IdToken = t.id_token });
            }
        }
    }
}
