using UnityEngine;
using UnityEngine.InputSystem;

namespace Veil.Match
{
    /// <summary>
    /// Gyroscope aiming for phones: turning/tilting the device rotates the camera (and therefore aim).
    /// Readings are screen-orientation compensated by the Input System, so x = screen right, y = screen up.
    /// </summary>
    public static class GyroAim
    {
        public enum Mode { Off = 0, WhileFiring = 1, Always = 2 }

        private const float Deadzone = 0.015f; // rad/s, removes sensor drift when the phone is still

        private static UnityEngine.InputSystem.Gyroscope Device => UnityEngine.InputSystem.Gyroscope.current;

        public static bool Available => Device != null;

        public static void SetEnabled(bool on)
        {
            var g = Device;
            if (g == null) return;
            InputSystem.settings.compensateForScreenOrientation = true;
            if (on && !g.enabled) InputSystem.EnableDevice(g);
            else if (!on && g.enabled) InputSystem.DisableDevice(g);
            if (on) g.samplingFrequency = 60;
        }

        /// <summary>Camera rotation this frame in degrees: x = yaw (right +), y = pitch up (+).</summary>
        public static Vector2 ReadDegrees(float dt, float sensitivity, bool invertX, bool invertY)
        {
            var g = Device;
            if (g == null || !g.enabled) return Vector2.zero;
            Vector3 w = g.angularVelocity.ReadValue();
            if (Mathf.Abs(w.x) < Deadzone) w.x = 0;
            if (Mathf.Abs(w.y) < Deadzone) w.y = 0;
            float yaw = -w.y * Mathf.Rad2Deg * dt * sensitivity;   // turning the phone right = look right
            float up = w.x * Mathf.Rad2Deg * dt * sensitivity;     // tilting the top edge back = look up
            return new Vector2(invertX ? -yaw : yaw, invertY ? -up : up);
        }
    }
}
