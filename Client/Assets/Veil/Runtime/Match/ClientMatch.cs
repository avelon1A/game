using System;
using System.Collections.Generic;
using UnityEngine;
using Veil.Sim;
using EventType = Veil.Sim.EventType;

namespace Veil.Match
{
    /// <summary>Source of authoritative match data: the in-process sim (offline) or the server (online).</summary>
    public interface IMatchDriver
    {
        int LocalPlayerId { get; }
        List<RosterEntry> Roster { get; }
        int MatchSeconds { get; }
        bool Autopilot { get; }
        bool IsOnline { get; }
        Action<Snapshot> OnSnapshot { get; set; }
        Action<List<SimEvent>> OnEvents { get; set; }
        Action<List<PlayerResult>> OnEnd { get; set; }
        void SendInput(InputCmd cmd);
        void Poll();
        void DebugSkip(float seconds);
        void Dispose();
    }

    /// <summary>
    /// Client-side match state shared by offline and online play:
    /// fixed-rate input sampling, prediction + reconciliation, snapshot history for interpolation,
    /// public pickups, and the event stream for the views/HUD.
    /// </summary>
    public sealed class ClientMatch
    {
        public readonly IMatchDriver Driver;
        public readonly MapData Map;
        public readonly PlayerState Predicted = new PlayerState();
        public readonly Dictionary<int, Pickup> Pickups = new Dictionary<int, Pickup>();
        public readonly List<Snapshot> History = new List<Snapshot>();
        public Snapshot Latest { get; private set; }
        public List<PlayerResult> Results { get; private set; }
        public bool Ended => Results != null;
        public bool Paused;

        /// <summary>Optional test input source (replaces keyboard/mouse).</summary>
        public System.Func<float, InputCmd> ScriptedInput;

        // prediction diagnostics
        public int Corrections;
        public float CorrectionSum, CorrectionMax;

        public event Action<SimEvent> Event;
        public event Action<List<PlayerResult>> MatchEnded;

        public int LocalId => Driver.LocalPlayerId;
        public List<RosterEntry> Roster => Driver.Roster;

        /// <summary>Interpolation factor between the previous and current predicted tick.</summary>
        public float Alpha { get; private set; }
        public Vector3 PrevPredictedPos { get; private set; }
        public Vector3 PredictedPos => new Vector3(Predicted.Pos.X, Predicted.H, Predicted.Pos.Y);
        public Vector3 CorrectionOffset;

        /// <summary>Estimated server time right now and the delayed time used for rendering remote avatars.</summary>
        public float ServerNow => Time.unscaledTime + _clockOffset;
        public float RenderTime => ServerNow - InterpDelay;
        public float InterpDelay => Driver.IsOnline ? 0.1f : GameConfig.Dt * 1.2f;

        private readonly List<InputCmd> _pending = new List<InputCmd>();
        private readonly InputCollector _input;
        private float _acc;
        private int _seq;
        private float _clockOffset;
        private bool _hasClock;

        public ClientMatch(IMatchDriver driver, MapData map, InputCollector input)
        {
            Driver = driver;
            Map = map;
            _input = input;
            driver.OnSnapshot = HandleSnapshot;
            driver.OnEvents = HandleEvents;
            driver.OnEnd = r => { Results = r; MatchEnded?.Invoke(r); };
        }

        public string NameOf(int id)
        {
            foreach (var r in Roster) if (r.Id == id) return r.Name;
            return "?";
        }

        public RosterEntry Entry(int id)
        {
            foreach (var r in Roster) if (r.Id == id) return r;
            return null;
        }

        /// <summary>Your squad (0..3).</summary>
        public int LocalSquad => Entry(LocalId)?.Squad ?? 0;
        public int SquadOf(int id) => Entry(id)?.Squad ?? -1;
        public bool IsAlly(int id) => id >= 0 && SquadOf(id) == LocalSquad;

        /// <summary>Your squadmates (roster order), excluding you.</summary>
        public List<RosterEntry> Squadmates()
        {
            var list = new List<RosterEntry>();
            int sq = LocalSquad;
            foreach (var r in Roster) if (r.Squad == sq && r.Id != LocalId) list.Add(r);
            return list;
        }

        /// <summary>Called every frame.</summary>
        /// <summary>Yaw the blaster fires along (from the player toward the crosshair target).</summary>
        public float AimYaw { get; private set; }

        public void Update(float dt, float cameraYaw, bool inputEnabled) => Update(dt, cameraYaw, cameraYaw, inputEnabled);

        public void Update(float dt, float cameraYaw, float aimYaw, bool inputEnabled)
        {
            Driver.Poll();
            if (Ended) return;
            if (Paused && !Driver.IsOnline) return;
            _input.Collect(inputEnabled && !Paused);

            _acc += dt;
            if (_acc > 0.25f) _acc = 0.25f;
            while (_acc >= GameConfig.Dt)
            {
                _acc -= GameConfig.Dt;
                ClientTick(cameraYaw, aimYaw, inputEnabled && !Paused);
                if (Ended) break;
            }
            Alpha = _acc / GameConfig.Dt;
            CorrectionOffset = Vector3.Lerp(CorrectionOffset, Vector3.zero, 1 - Mathf.Exp(-12f * dt));
        }

        private void ClientTick(float cameraYaw, float aimYaw, bool inputEnabled)
        {
            AimYaw = aimYaw;
            var cmd = ScriptedInput != null ? ScriptedInput(cameraYaw) : _input.Sample(cameraYaw, inputEnabled);
            if (ScriptedInput == null) cmd.Yaw = aimYaw;   // movement is camera-relative, shots go to the crosshair
            cmd.Seq = ++_seq;
            cmd = Protocol.Quantize(cmd);
            PrevPredictedPos = PredictedPos;
            if (!Driver.Autopilot && Latest != null)
            {
                _pending.Add(cmd);
                if (_pending.Count > 90) _pending.RemoveAt(0);
                Movement.Step(Predicted, cmd, Map, GameConfig.Dt);
            }
            Driver.SendInput(cmd);
        }

        private void HandleSnapshot(Snapshot s)
        {
            if (Latest != null && s.Tick <= Latest.Tick) return;
            float est = s.Time - Time.unscaledTime;
            if (!_hasClock) { _clockOffset = est; _hasClock = true; }
            else if (est > _clockOffset) _clockOffset = Mathf.Lerp(_clockOffset, est, 0.5f);
            else _clockOffset = Mathf.Lerp(_clockOffset, est, 0.05f);

            Latest = s;
            History.Add(s);
            while (History.Count > 40) History.RemoveAt(0);

            // ---- reconcile the local player ----
            Vector3 before = PredictedPos;
            bool wasAlive = Predicted.Alive;
            SnapshotBuilder.CopySelf(s.Self, Predicted);
            if (!Driver.Autopilot)
            {
                _pending.RemoveAll(c => c.Seq <= s.Self.LastSeq);
                foreach (var c in _pending) Movement.Step(Predicted, c, Map, GameConfig.Dt);
            }
            Vector3 after = PredictedPos;
            Vector3 err = before - after;
            if (!Driver.Autopilot && wasAlive && Predicted.Alive && History.Count > 1)
            {
                Corrections++;
                CorrectionSum += err.magnitude;
                CorrectionMax = Mathf.Max(CorrectionMax, err.magnitude);
            }
            if (wasAlive && Predicted.Alive && err.sqrMagnitude < 4f) CorrectionOffset += err;
            else { CorrectionOffset = Vector3.zero; PrevPredictedPos = after; }
        }

        private void HandleEvents(List<SimEvent> events)
        {
            foreach (var e in events)
            {
                switch (e.Type)
                {
                    case EventType.PickupSpawned:
                        Pickups[e.A] = new Pickup { Id = e.A, Type = (PickupType)e.B, Pos = e.Pos, Spot = e.Value };
                        break;
                    case EventType.PickupCollected:
                        Pickups.Remove(e.B);
                        break;
                }
                Event?.Invoke(e);
            }
        }

        /// <summary>Samples an avatar's interpolated state at render time.</summary>
        public bool SampleAvatar(int avatarId, float time, out AvatarSnap a, out AvatarSnap b, out float t)
        {
            a = null; b = null; t = 0;
            for (int i = History.Count - 1; i >= 0; i--)
            {
                var s = History[i];
                if (s.Time > time) continue;
                a = Find(s, avatarId);
                if (i + 1 < History.Count)
                {
                    var n = History[i + 1];
                    b = Find(n, avatarId);
                    if (a != null && b != null) t = Mathf.Clamp01((time - s.Time) / Mathf.Max(0.0001f, n.Time - s.Time));
                }
                break;
            }
            if (a == null && Latest != null) a = Find(Latest, avatarId);
            return a != null;
        }

        private static AvatarSnap Find(Snapshot s, int id)
        {
            foreach (var x in s.Avatars) if (x.AvatarId == id) return x;
            return null;
        }

        public void Dispose() => Driver.Dispose();
    }
}
