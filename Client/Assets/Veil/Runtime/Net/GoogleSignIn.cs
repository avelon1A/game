using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace Veil.Net
{
    /// <summary>
    /// "Sign in with Google" on Android (Credential Manager account picker, Java side: Plugins/Android/RiloGoogleSignIn.java).
    /// Returns a Google ID token that the Rilo server verifies. Other platforms: not supported (guest accounts only).
    /// </summary>
    public static class GoogleSignIn
    {
        public struct Result
        {
            public bool Ok;
            public string IdToken, Email, Name, Error;
            public bool Cancelled => Error == "cancelled";
        }

        public static bool Supported => Application.platform == RuntimePlatform.Android;

        private static readonly ConcurrentQueue<Action> Main = new ConcurrentQueue<Action>();

        /// <summary>Call every frame (results arrive on a Java thread).</summary>
        public static void Pump() { while (Main.TryDequeue(out var a)) a(); }

        /// <param name="webClientId">the OAuth *Web* client id (audience the server checks)</param>
        public static void SignIn(string webClientId, Action<Result> done)
        {
            if (!Supported) { done(new Result { Error = "Google sign-in is available in the Android app" }); return; }
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
            if (!Supported) return;
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
    }
}
