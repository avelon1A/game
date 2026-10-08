using UnityEngine;
using UnityEngine.InputSystem;
using Veil.Sim;

namespace Veil.Match
{
    /// <summary>
    /// Reads keyboard/mouse (new Input System) and the on-screen touch controls every frame and
    /// produces one <see cref="InputCmd"/> per 30 Hz tick. One-shot presses are latched between
    /// ticks so none are lost.
    /// </summary>
    public sealed class InputCollector
    {
        private Buttons _latched;

        public void Collect(bool enabled)
        {
            if (!enabled) return;
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.qKey.wasPressedThisFrame) _latched |= Buttons.Dash;
                if (kb.eKey.wasPressedThisFrame) _latched |= Buttons.Pulse;
                if (kb.rKey.wasPressedThisFrame) _latched |= Buttons.Decoy;
                if (kb.digit1Key.wasPressedThisFrame) _latched |= Buttons.Buy1;
                if (kb.digit2Key.wasPressedThisFrame) _latched |= Buttons.Buy2;
                if (kb.digit3Key.wasPressedThisFrame) _latched |= Buttons.Buy3;
                if (kb.spaceKey.wasPressedThisFrame) _latched |= Buttons.Jump;
                if (kb.xKey.wasPressedThisFrame) _latched |= Buttons.Switch;   // gun <-> fists
                if (kb.gKey.wasPressedThisFrame) _latched |= Buttons.Grenade;
                if (kb.zKey.wasPressedThisFrame || kb.tKey.wasPressedThisFrame) _latched |= Buttons.Ping;
            }
            var m = Mouse.current;
            if (!VirtualInput.Active && m != null && m.leftButton.wasPressedThisFrame && Cursor.lockState == CursorLockMode.Locked) _latched |= Buttons.Fire;
            if (!VirtualInput.Active && m != null && m.middleButton.wasPressedThisFrame && Cursor.lockState == CursorLockMode.Locked) _latched |= Buttons.Ping;
            _latched |= VirtualInput.TakeLatched();
        }

        public InputCmd Sample(float cameraYaw, bool enabled)
        {
            var cmd = new InputCmd { Yaw = cameraYaw };
            if (!enabled) { _latched = Buttons.None; VirtualInput.TakeLatched(); return cmd; }

            float x = 0, y = 0;
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) y += 1;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) y -= 1;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) x += 1;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) x -= 1;
            }
            var b = _latched;
            // touch joystick (pushed all the way = sprint)
            if (VirtualInput.Active && VirtualInput.Move.sqrMagnitude > 0.0025f && x == 0 && y == 0)
            {
                x = VirtualInput.Move.x; y = VirtualInput.Move.y;
                if (VirtualInput.Move.magnitude > 0.92f) b |= Buttons.Sprint;
            }
            var local = new Vec2(x, y).ClampLength(1f);
            var world = Vec2.RotateYaw(local, cameraYaw);
            cmd.MoveX = world.X;
            cmd.MoveY = world.Y;

            if (kb != null)
            {
                if (kb.spaceKey.isPressed) b |= Buttons.Jump;
                if (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed) b |= Buttons.Sprint;
            }
            var m = Mouse.current;
            if (!VirtualInput.Active && m != null && m.leftButton.isPressed && Cursor.lockState == CursorLockMode.Locked) b |= Buttons.Fire;
            if (VirtualInput.FireHeld) b |= Buttons.Fire;
            if (VirtualInput.JumpHeld) b |= Buttons.Jump;
            cmd.Buttons = b;
            _latched = Buttons.None;
            return cmd;
        }
    }
}
