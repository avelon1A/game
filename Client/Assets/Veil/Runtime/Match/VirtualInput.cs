using UnityEngine;
using Veil.Sim;

namespace Veil.Match
{
    /// <summary>
    /// Touch-control state written by the on-screen controls and read by <see cref="InputCollector"/>.
    /// Mirrors the keyboard/mouse bindings so desktop and mobile produce identical InputCmds.
    /// </summary>
    public static class VirtualInput
    {
        public static bool Active;              // touch controls on screen
        public static Vector2 Move;             // joystick, camera-relative (x right, y forward), |v| <= 1
        public static Vector2 LookDelta;        // pixels this frame (consumed by the camera)
        public static bool FireHeld, JumpHeld, TalkHeld;
        public static bool ScopeOn;             // sniper scope (toggle button)
        private static Buttons _latched;

        public static void Press(Buttons b) => _latched |= b;

        /// <summary>Returns and clears one-shot presses.</summary>
        public static Buttons TakeLatched()
        {
            var b = _latched;
            _latched = Buttons.None;
            return b;
        }

        public static Vector2 TakeLook()
        {
            var d = LookDelta;
            LookDelta = Vector2.zero;
            return d;
        }

        public static void Reset()
        {
            Move = Vector2.zero; LookDelta = Vector2.zero; FireHeld = JumpHeld = TalkHeld = ScopeOn = false; _latched = Buttons.None;
        }
    }

    public static class Platform
    {
        private static int _forced = -1;

        /// <summary>True on phones/tablets, or on desktop when launched with -mobile-ui (for testing).</summary>
        public static bool IsMobile
        {
            get
            {
                if (_forced < 0)
                {
                    _forced = 0;
                    foreach (var a in System.Environment.GetCommandLineArgs()) if (a == "-mobile-ui") _forced = 1;
                }
                return Application.isMobilePlatform || _forced == 1;
            }
        }
    }
}
