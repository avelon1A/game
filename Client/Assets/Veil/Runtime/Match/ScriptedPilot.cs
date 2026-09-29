using System.Collections.Generic;
using Veil.Sim;

namespace Veil.Match
{
    /// <summary>
    /// Test driver that produces "human" inputs from the client's own knowledge (snapshots), so
    /// the real input → prediction → reconciliation path is exercised offline and online.
    /// </summary>
    public sealed class ScriptedPilot
    {
        private readonly ClientMatch _m;
        private readonly Rng _rng = new Rng(4242);
        private readonly List<Vec2> _path = new List<Vec2>();
        private int _pathIdx, _zone;
        private float _t, _nextGoal, _repath;
        private Vec2 _goal;

        public ScriptedPilot(ClientMatch m) { _m = m; }

        public InputCmd Next(float cameraYaw)
        {
            _t += GameConfig.Dt;
            var me = _m.Predicted;
            var cmd = new InputCmd { Yaw = me.Yaw };
            if (!me.Alive || _m.Latest == null) return cmd;

            if (_t >= _nextGoal)
            {
                var zones = _m.Map.Zones;
                _goal = zones[_zone++ % zones.Count].Center + _rng.InsideCircle(3f);
                _nextGoal = _t + 16f;
                _repath = 0;
            }
            _repath -= GameConfig.Dt;
            if (_repath <= 0 || _path.Count == 0)
            {
                _m.Map.Nav.FindPath(me.Pos, _goal, _path);
                _pathIdx = 0;
                _repath = 1.5f;
            }
            while (_pathIdx < _path.Count && Vec2.Dist(_path[_pathIdx], me.Pos) < 1.2f) _pathIdx++;
            Vec2 wp = _pathIdx < _path.Count ? _path[_pathIdx] : _goal;
            Vec2 dir = (wp - me.Pos).Normalized;
            cmd.MoveX = dir.X;
            cmd.MoveY = dir.Y;
            cmd.Yaw = dir.LengthSq > 0 ? dir.Yaw : me.Yaw;

            // shoot at the nearest fully visible avatar
            AvatarSnap best = null;
            float bd = 22f * 22f;
            foreach (var a in _m.Latest.Avatars)
            {
                if (a.Vis != Visibility.Full || (a.Flags & AvatarFlags.MyDecoy) != 0) continue;
                float d = Vec2.DistSq(a.Pos, me.Pos);
                if (d < bd) { bd = d; best = a; }
            }
            if (best != null)
            {
                cmd.Yaw = (best.Pos - me.Pos).Yaw;
                cmd.Buttons |= Buttons.Fire;
            }
            else cmd.Buttons |= Buttons.Sprint;

            int tick = (int)(_t * GameConfig.TickRate);
            if (tick % 95 == 0) cmd.Buttons |= Buttons.Jump;
            if (tick % 160 == 40) cmd.Buttons |= Buttons.Dash;
            if (tick % 300 == 120) cmd.Buttons |= Buttons.Pulse;
            if (tick % 450 == 200) cmd.Buttons |= Buttons.Decoy;
            if (me.ZoneId >= 0 && _m.Map.Zones[me.ZoneId].Type == ZoneType.Market && tick % 60 == 0) cmd.Buttons |= Buttons.Buy2;
            return cmd;
        }
    }
}
