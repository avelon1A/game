using System.Runtime.InteropServices;
using UnityEngine;

namespace Veil.App
{
    /// <summary>Opens the phone's share sheet (WhatsApp, Telegram, Messages…) with a text; copies it everywhere else.</summary>
    public static class NativeShare
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void RiloShareText(string text);
#endif

        /// <summary>Returns false when there is no share sheet (desktop / editor): the text was copied instead.</summary>
        public static bool Share(string text, string title = "Share")
        {
            GUIUtility.systemCopyBuffer = text;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var intentClass = new AndroidJavaClass("android.content.Intent"))
                using (var intent = new AndroidJavaObject("android.content.Intent"))
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    intent.Call<AndroidJavaObject>("setAction", intentClass.GetStatic<string>("ACTION_SEND"));
                    intent.Call<AndroidJavaObject>("setType", "text/plain");
                    intent.Call<AndroidJavaObject>("putExtra", intentClass.GetStatic<string>("EXTRA_TEXT"), text);
                    var chooser = intentClass.CallStatic<AndroidJavaObject>("createChooser", intent, title);
                    activity.Call("startActivity", chooser);
                }
                return true;
            }
            catch (System.Exception e) { Debug.LogWarning("[VEIL] share failed: " + e.Message); return false; }
#elif UNITY_IOS && !UNITY_EDITOR
            RiloShareText(text);
            return true;
#else
            return false;
#endif
        }
    }
}
