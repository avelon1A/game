using System;
using System.Collections.Generic;

namespace Veil.Sim
{
    /// <summary>
    /// Believable, beatable bots (GDD §20-21):
    ///   Position + Known information + Objective + Resources + Nearby players + Risk = Decision
    /// Bots only perceive what <see cref="Visibility"/> lets them see, so Decoys fool them too.
    /// </summary>
    public sealed class BotBrain
    {
        private enum Goal { Wander, HoldZone, Pickup, Fight, Chase, Flee, Vault, Market, Center }

        private struct Seen
        {
            public int AvatarId, Owner;
            public Vec2 Pos, Vel;
            public float Time, Health;
            public bool Full;
        }

        private readonly MatchSim _sim;
        private readonly PlayerState _p;
        private readonly Rng _rng;
        private readonly float _skill;
        private readonly Dictionary<int, Seen> _seen = new Dictionary<int, Seen>();
        private readonly List<Vec2> _path = new List<Vec2>();
        private readonly List<int> _stale = new List<int>();

        private Goal _goal = Goal.Wander;
        private Vec2 _goalPos;
        private int _goalAvatar = -1;
        private int _goalZone = -1;
        private int _pathIdx;
        private Vec2 _pathGoal;
        private float _thinkT, _repathT, _stuckT, _strafeT, _orbitT, _abilityT;
        private int _strafeDir = 1;
        private Vec2 _lastPos;
        private Vec2 _orbitOffset;
        private int _seq;
        private bool _wantDash, _wantDecoy, _wantPulse, _wantJump;
        private int _wantBuy;

        public PlayerState Player => _p;
        public string GoalName => _goal.ToString();

        public BotBrain(MatchSim sim, PlayerState p, int seed)
        {
            _sim = sim;
            _p = p;
            _rng = new Rng(seed);
            _skill = _rng.Range(0.55f, 1f);
            _thinkT = _rng.Range(0, 0.5f);
        }

        private BotKind Kind => _p.BotKind;

        public void Update(float dt)
        {
            var cmd = new InputCmd { Yaw = _p.Yaw };
            if (_p.Alive)
            {
                Perceive();
                _thinkT -= dt;
                if (_thinkT <= 0)
                {
                    Think();
                    _thinkT = _rng.Range(0.25f, 0.45f);
                }
                cmd = Act(dt);
            }
            else
            {
                _path.Clear();
                _goal = Goal.Wander;
            }
            cmd.Seq = Math.Max(++_seq, _p.LastSeq + 1);
            _seq = cmd.Seq;
            _sim.SubmitInput(_p.Id, cmd);
        }

        // ------------------------------------------------------------------ perception

        private void Perceive()
        {
            float now = _sim.Time;
            foreach (var o in _sim.Players)
            {
                if (o == _p || MatchSim.Allies(o, _p)) continue;   // squadmates are never targets
                byte vis = Visibility.OfPlayer(_sim, _p, o);
                if (vis == Visibility.None) continue;
                _seen[o.Id] = new Seen { AvatarId = o.Id, Owner = o.Id, Pos = o.Pos, Vel = o.Vel, Time = now, Health = o.HealthFrac, Full = vis == Visibility.Full };
            }
            foreach (var d in _sim.Decoys)
            {
                if (MatchSim.Allies(_sim.Players[d.Owner], _p)) continue;
                byte vis = Visibility.OfDecoy(_sim, _p, d);
                if (vis == Visibility.None) continue;
                _seen[d.AvatarId] = new Seen { AvatarId = d.AvatarId, Owner = d.Owner, Pos = d.Pos, Vel = d.Vel, Time = now, Health = _sim.Players[d.Owner].HealthFrac, Full = vis == Visibility.Full };
            }
            _stale.Clear();
            foreach (var kv in _seen)
            {
                bool gone = now - kv.Value.Time > 6f;
                if (kv.Key < 1000 && !_sim.Players[kv.Key].Alive && now - kv.Value.Time > 0.5f) gone = true;
                if (kv.Key >= 1000 && !DecoyAlive(kv.Key)) gone = gone || now - kv.Value.Time > 0.3f;
                if (gone) _stale.Add(kv.Key);
            }
            foreach (int k in _stale) _seen.Remove(k);
        }

        private bool DecoyAlive(int avatar)
        {
            foreach (var d in _sim.Decoys) if (d.AvatarId == avatar) return true;
            return false;
        }

        private bool VisibleNow(Seen s) => s.Full && _sim.Time - s.Time < 0.05f;

        // ------------------------------------------------------------------ decision

        private void Think()
        {
            float bestScore = float.MinValue;
            Goal best = Goal.Wander;
            Vec2 bestPos = _goalPos;
            int bestAvatar = -1, bestZone = -1;

            void Consider(Goal g, float score, Vec2 pos, int avatar = -1, int zone = -1)
            {
                score += _rng.Range(-6f, 6f);
                if (g == _goal && (avatar < 0 || avatar == _goalAvatar)) score += 14f; // hysteresis
                if (score > bestScore) { bestScore = score; best = g; bestPos = pos; bestAvatar = avatar; bestZone = zone; }
            }

            float circle = _sim.CircleRadius;
            bool collapsing = circle < GameConfig.CircleStartRadius - 1;

            // ---- survival: stay inside the circle ----
            if (collapsing && _p.Pos.Length > circle - 4f)
                Consider(Goal.Center, 1000, Vec2.Zero);

            // ---- threats & targets ----
            Seen? threat = null, target = null;
            float bestTarget = float.MinValue;
            foreach (var s in _seen.Values)
            {
                float d = Vec2.Dist(s.Pos, _p.Pos);
                bool attackedMe = _p.LastAttacker == s.Owner && _p.SinceDamage < 3f;
                if (s.Full && d < 22f && (attackedMe || threat == null)) threat = s;
                if (!s.Full) continue;
                float t = 40f - d;
                if (attackedMe) t += 35f;
                if (Kind == BotKind.Opportunist) t += (1f - s.Health) * 60f;
                if (Kind == BotKind.Hunter) t += 20f;
                if (IsNemesis(s.Owner)) t += 25f;
                if (t > bestTarget) { bestTarget = t; target = s; }
            }

            float hp = _p.HealthFrac;
            if (threat.HasValue && hp < 0.35f && Kind != BotKind.Defender)
            {
                Vec2 away = (_p.Pos - threat.Value.Pos).Normalized;
                Consider(Goal.Flee, 95, ClampInside(_p.Pos + away * 18f), threat.Value.AvatarId);
            }

            if (target.HasValue)
            {
                var t = target.Value;
                float d = Vec2.Dist(t.Pos, _p.Pos);
                float fight = Kind switch
                {
                    BotKind.Hunter => 62,
                    BotKind.Opportunist => 22 + (1 - t.Health) * 70,
                    BotKind.Defender => _p.ZoneId >= 0 ? 65 : 22,
                    _ => 18,
                };
                if (_p.LastAttacker == t.Owner && _p.SinceDamage < 3f) fight += 35;
                if (IsNemesis(t.Owner)) fight += 25;
                fight -= (1 - hp) * 40;
                if (d > 26f) fight -= 25;
                if (_sim.Phase == MatchPhase.Exploration) fight -= 20;
                Consider(Goal.Fight, fight, t.Pos, t.AvatarId);
            }
            else if (Kind == BotKind.Hunter || HasObjective(ObjectiveType.Nemesis))
            {
                // chase the freshest ping
                foreach (var s in _seen.Values)
                {
                    if (s.Full) continue;
                    float score = (Kind == BotKind.Hunter ? 40 : 20) + (IsNemesis(s.Owner) ? 25 : 0) - Vec2.Dist(s.Pos, _p.Pos) * 0.3f;
                    Consider(Goal.Chase, score, s.Pos, s.AvatarId);
                }
            }

            // ---- objectives ----
            ConsiderObjective(_p.Primary, 60, Consider);
            ConsiderObjective(_p.Secondary, 38, Consider);
            ConsiderObjective(_sim.Squads[_p.Squad].Objective, 44, Consider);

            // ---- extraction mode chain: own objective → Vault → extraction (and contest other squads there) ----
            if (GameConfig.ExtractionMode)
            {
                var sq = _sim.Squads[_p.Squad];
                float drive = Kind switch { BotKind.Explorer => 82, BotKind.Defender => 78, BotKind.Collector => 72, BotKind.Opportunist => 64, _ => 58 };
                if (_sim.ExtractRevealed)
                {
                    float de = Vec2.Dist(_sim.ExtractPos, _p.Pos);
                    bool rival = _sim.ExtractController >= 0 && _sim.ExtractController != _p.Squad;
                    if (sq.VaultDone) Consider(Goal.Vault, 96 - de * 0.08f, _sim.ExtractPos);
                    else if (rival || Kind == BotKind.Hunter || Kind == BotKind.Opportunist) Consider(Goal.Vault, (rival ? 88 : 60) - de * 0.12f, _sim.ExtractPos);
                }
                if (sq.Nodes.Count > 0)
                {
                    // instability: squadmates split over the nodes (by id), the rest guard the terminal
                    int slot = 0;
                    foreach (var o in _sim.Players) if (o.Squad == _p.Squad && o.Id < _p.Id && o.IsBot && o.Alive) slot++;   // humans choose for themselves
                    if (sq.Nodes.Count > 0)
                    {
                        var n = sq.Nodes[slot % sq.Nodes.Count];
                        Consider(Goal.Vault, 160 - Vec2.Dist(n.Pos, _p.Pos) * 0.05f, n.Pos);   // above fighting / fleeing
                    }
                }
                if (sq.Stage < 4)
                {
                    float ds = Vec2.Dist(sq.Site, _p.Pos);
                    if (MatchSim.TaskOf(sq.Stage) == ChainTask.Collect)
                    {
                        var core = NearestPickup(PickupType.Core, 200f);
                        if (core != null) Consider(Goal.Pickup, drive - Vec2.Dist(core.Pos, _p.Pos) * 0.2f, core.Pos);
                    }
                    else if (MatchSim.TaskOf(sq.Stage) == ChainTask.Hack)
                    {
                        // home terminal (quiet) or the central one (fast + bonus): aggressive bots and squads already ahead at the centre go there
                        bool centre = Kind == BotKind.Hunter || Kind == BotKind.Opportunist || sq.CenterProg > sq.HomeProg + 0.1f;
                        var t = centre ? _sim.CenterTerminal : sq.Home;
                        Consider(Goal.Vault, drive - Vec2.Dist(t, _p.Pos) * 0.12f, t);
                    }
                    else Consider(Goal.Vault, drive + (sq.Stage == 3 ? 8 : 0) - ds * 0.12f, sq.Site);
                }
            }

            // ---- downed: crawl to the nearest standing squadmate; standing: go revive a downed one ----
            foreach (var o in _sim.Players)
            {
                if (o == _p || !o.Alive || !MatchSim.Allies(o, _p)) continue;
                float d = Vec2.Dist(o.Pos, _p.Pos);
                if (_p.Downed && !o.Downed) Consider(Goal.Wander, 95 - d * 0.3f, o.Pos);
                else if (!_p.Downed && o.Downed && d < 45f) Consider(Goal.Wander, (Kind == BotKind.Defender ? 82 : 72) - d * 0.5f, o.Pos);
            }

            // ---- squad: regroup when drifting far from the nearest living squadmate; back up a mate in a fight ----
            {
                PlayerState mate = null; float md = float.MaxValue;
                foreach (var o in _sim.Players)
                {
                    if (o == _p || !o.Alive || !MatchSim.Allies(o, _p)) continue;
                    float d = Vec2.Dist(o.Pos, _p.Pos);
                    if (d < md) { md = d; mate = o; }
                }
                if (mate != null)
                {
                    if (md > 28f) Consider(Goal.Wander, 30 + (md - 28f) * 0.6f, mate.Pos);
                    if (mate.SinceDamage < 2f && mate.LastAttacker >= 0 && md < 40f)
                    {
                        var att = _sim.Players[mate.LastAttacker];
                        if (att.Alive && !MatchSim.Allies(att, _p)) Consider(Goal.Chase, 52 - md * 0.4f, att.Pos, att.Id);
                    }
                }
            }

            // ---- energy management ----
            if (_p.Energy < 30)
            {
                var orb = NearestPickup(PickupType.Orb, 40f);
                if (orb != null) Consider(Goal.Pickup, 48 - Vec2.Dist(orb.Pos, _p.Pos) * 0.4f, orb.Pos);
                var rz = _sim.Map.Zones[_sim.ReactorZone];
                Consider(Goal.HoldZone, 36 - Vec2.Dist(rz.Center, _p.Pos) * 0.2f, rz.Center, -1, rz.Id);
            }

            // ---- personality flavour ----
            if (Kind == BotKind.Collector)
            {
                var near = NearestPickup(PickupType.Core, 60f) ?? NearestPickup(PickupType.Orb, 30f);
                if (near != null) Consider(Goal.Pickup, 50 - Vec2.Dist(near.Pos, _p.Pos) * 0.35f, near.Pos);
            }
            if (Kind == BotKind.Explorer || Kind == BotKind.Defender)
            {
                for (int i = 0; i < _sim.Zones.Length; i++)
                {
                    var def = _sim.Map.Zones[i];
                    if (!def.Capturable) continue;
                    var z = _sim.Zones[i];
                    float s = (Kind == BotKind.Defender ? 45 : 32) - Vec2.Dist(def.Center, _p.Pos) * 0.25f;
                    if (z.Squad == _p.Squad) s += Kind == BotKind.Defender ? 18 : -20;
                    Consider(Goal.HoldZone, s, def.Center, -1, i);
                }
            }
            {
                var orb = NearestPickup(PickupType.Orb, 14f);
                if (orb != null && _p.Energy < 90) Consider(Goal.Pickup, 26 - Vec2.Dist(orb.Pos, _p.Pos), orb.Pos);
            }

            if (best == Goal.Wander || bestScore < 12)
                Consider(Goal.Wander, 12, _goal == Goal.Wander && Vec2.Dist(_goalPos, _p.Pos) > 3 ? _goalPos : ClampInside(_sim.Map.Nav.RandomWalkable(_rng)));

            _goal = best;
            _goalPos = bestPos;
            _goalAvatar = bestAvatar;
            _goalZone = bestZone;

            PlanAbilities(threat, target);
        }

        private void ConsiderObjective(ObjectiveState o, float weight, Action<Goal, float, Vec2, int, int> consider)
        {
            if (o.Done) return;
            switch (o.Type)
            {
                case ObjectiveType.TowerControl:
                {
                    var tz = _sim.Map.Zones[_sim.TowerZone];
                    float s = weight - Vec2.Dist(tz.Center, _p.Pos) * 0.15f;
                    if (_sim.Zones[_sim.TowerZone].Squad == _p.Squad) s += 5;
                    consider(Goal.HoldZone, s, tz.Center, -1, tz.Id);
                    break;
                }
                case ObjectiveType.CollectCores:
                {
                    var c = NearestPickup(PickupType.Core, 200f);
                    if (c != null) consider(Goal.Pickup, weight + 4 - Vec2.Dist(c.Pos, _p.Pos) * 0.2f, c.Pos, -1, -1);
                    break;
                }
                case ObjectiveType.VaultRaid:
                {
                    if (_p.Keys >= GameConfig.VaultKeys)
                    {
                        var vz = _sim.Zones[_sim.VaultZone];
                        var vd = _sim.Map.Zones[_sim.VaultZone];
                        if (!vz.Locked && vz.Cooldown <= 3f) consider(Goal.Vault, weight + 15, vd.Center, -1, vd.Id);
                    }
                    else
                    {
                        var k = NearestPickup(PickupType.Key, 200f);
                        if (k != null) consider(Goal.Pickup, weight + 2 - Vec2.Dist(k.Pos, _p.Pos) * 0.2f, k.Pos, -1, -1);
                        if (_p.Energy >= GameConfig.MarketKeyCost + 5)
                        {
                            var mz = _sim.Map.Zones[_sim.MarketZone];
                            consider(Goal.Market, weight - Vec2.Dist(mz.Center, _p.Pos) * 0.2f, mz.Center, -1, mz.Id);
                        }
                    }
                    break;
                }
                case ObjectiveType.CaptureTwo:
                {
                    for (int i = 0; i < _sim.Zones.Length; i++)
                    {
                        var def = _sim.Map.Zones[i];
                        if (!def.Capturable || (_p.CapturedMask & (1 << i)) != 0) continue;
                        consider(Goal.HoldZone, weight - Vec2.Dist(def.Center, _p.Pos) * 0.2f, def.Center, -1, i);
                    }
                    break;
                }
                case ObjectiveType.HighEnergy:
                {
                    if (_sim.Phase >= MatchPhase.Collapse && _p.Energy < o.Target + 10)
                    {
                        var orb = NearestPickup(PickupType.Orb, 60f);
                        if (orb != null) consider(Goal.Pickup, weight - Vec2.Dist(orb.Pos, _p.Pos) * 0.3f, orb.Pos, -1, -1);
                    }
                    break;
                }
            }
        }

        private bool HasObjective(ObjectiveType t) => (_p.Primary.Type == t && !_p.Primary.Done) || (_p.Secondary.Type == t && !_p.Secondary.Done);

        private bool IsNemesis(int owner) =>
            (_p.Primary.Type == ObjectiveType.Nemesis && _p.Primary.TargetPlayer == owner && !_p.Primary.Done) ||
            (_p.Secondary.Type == ObjectiveType.Nemesis && _p.Secondary.TargetPlayer == owner && !_p.Secondary.Done);

        private Pickup NearestPickup(PickupType type, float maxDist)
        {
            Pickup best = null;
            float bd = maxDist * maxDist;
            foreach (var pk in _sim.Pickups.Values)
            {
                if (pk.Type != type) continue;
                float d = Vec2.DistSq(pk.Pos, _p.Pos);
                if (d < bd) { bd = d; best = pk; }
            }
            return best;
        }

        private Vec2 ClampInside(Vec2 p)
        {
            float lim = MathF.Min(GameConfig.MapHalf - 6f, _sim.CircleRadius - 3f);
            if (p.Length > lim) p = p.Normalized * lim;
            return p;
        }

        private void PlanAbilities(Seen? threat, Seen? target)
        {
            _abilityT -= 0.35f;
            if (_abilityT > 0) return;
            bool fleeing = _goal == Goal.Flee;
            if (fleeing && _p.Energy >= GameConfig.DecoyCost && _p.DecoyCd <= 0 && _rng.Chance(0.7f)) { _wantDecoy = true; _abilityT = 1.5f; }
            else if (fleeing && _p.Energy >= GameConfig.DashCost && _p.DashCd <= 0) { _wantDash = true; _abilityT = 0.8f; }
            else if (Kind == BotKind.Opportunist && target.HasValue && _p.Energy > 60 && _p.DecoyCd <= 0 && _rng.Chance(0.25f)) { _wantDecoy = true; _abilityT = 2f; }

            // pulse to gather information: pings without full sight, or arriving at a zone
            if (_p.PulseCd <= 0 && _p.Energy >= GameConfig.PulseCost + 10)
            {
                bool pinged = false;
                foreach (var s in _seen.Values) if (!s.Full && Vec2.Dist(s.Pos, _p.Pos) < GameConfig.PulseRadius) { pinged = true; break; }
                bool arriving = _goal == Goal.HoldZone && Vec2.Dist(_goalPos, _p.Pos) < 16f;
                if ((pinged && _rng.Chance(0.5f)) || (arriving && _rng.Chance(0.25f)) || (Kind == BotKind.Hunter && _rng.Chance(0.08f)))
                    _wantPulse = true;
            }

            // market shopping
            if (_p.ZoneId == _sim.MarketZone && _p.BuyCd <= 0)
            {
                if (HasObjective(ObjectiveType.VaultRaid) && _p.Keys < GameConfig.VaultKeys && _p.Energy >= _sim.MarketPrice(_p, 3)) _wantBuy = 3;
                else if (_p.Shield <= 0 && _p.Energy >= _sim.MarketPrice(_p, 2) + 20 && _rng.Chance(0.5f)) _wantBuy = 2;
                else if (_p.Energy >= _sim.MarketPrice(_p, 1) + 40 && _rng.Chance(0.3f)) _wantBuy = 1;
            }
        }

        // ------------------------------------------------------------------ action

        private InputCmd Act(float dt)
        {
            var cmd = new InputCmd { Yaw = _p.Yaw };
            Vec2 dest = _goalPos;

            Seen? target = null;
            if ((_goal == Goal.Fight || _goal == Goal.Chase || _goal == Goal.Flee) && _goalAvatar >= 0 && _seen.TryGetValue(_goalAvatar, out var s))
            {
                target = s;
                if (_goal != Goal.Flee) dest = s.Pos;
            }
            bool selfDefense = false;
            if (!target.HasValue && _goal != Goal.Flee && !_p.Downed)
            {
                selfDefense = true;
                // busy with an objective: still shoot back at the closest visible enemy
                float bd = 24f;
                foreach (var e in _seen.Values)
                {
                    if (!e.Full || !VisibleNow(e)) continue;
                    float d = Vec2.Dist(e.Pos, _p.Pos);
                    if (d < bd) { bd = d; target = e; }
                }
            }

            // stay near zone centres with a lazy orbit so bots don't stack
            if (_goal == Goal.HoldZone || _goal == Goal.Vault || _goal == Goal.Market)
            {
                _orbitT -= dt;
                if (_orbitT <= 0)
                {
                    float rad = _goalZone >= 0 ? _sim.Map.Zones[_goalZone].Radius * 0.55f : 3f;
                    _orbitOffset = _rng.InsideCircle(_goal == Goal.Vault ? 1.5f : rad);
                    _orbitT = _rng.Range(2f, 4f);
                }
                dest = _goalPos + _orbitOffset;
                if (!_sim.Map.Nav.Walkable(dest)) dest = _goalPos;
            }

            // ---- hack nodes: shoot Destroy nodes in sight ----
            Vec2? node = null;
            var myNodes = _sim.Squads[_p.Squad].Nodes;
            if (!target.HasValue && myNodes.Count > 0)
            {
                float bn = 30f;
                foreach (var n in myNodes)
                {
                    if (n.Kind != NodeKind.Destroy) continue;
                    float d = Vec2.Dist(n.Pos, _p.Pos);
                    if (d < bn && _sim.Map.HasLineOfSight(_p.Pos, n.Pos)) { bn = d; node = n.Pos; }
                }
            }

            // ---- combat ----
            bool fighting = false;
            if (target.HasValue && VisibleNow(target.Value) && _goal != Goal.Flee)
            {
                var t = target.Value;
                float d = Vec2.Dist(t.Pos, _p.Pos);
                if (d < GameConfig.Weapon(_p.Look.Weapon).Range - 3f && _sim.Map.HasLineOfSight(_p.Pos, t.Pos))
                {
                    fighting = true;
                    float lead = d / GameConfig.Weapon(_p.Look.Weapon).Speed;
                    Vec2 aimAt = t.Pos + t.Vel * lead * _skill;
                    float err = (1.2f - _skill) * 14f;
                    float desired = (aimAt - _p.Pos).Yaw + _rng.Range(-err, err);
                    float turn = 540f * dt * (0.6f + _skill);
                    cmd.Yaw = _p.Yaw + MathUtil.Clamp(MathUtil.DeltaAngle(_p.Yaw, desired), -turn, turn);
                    if (MathF.Abs(MathUtil.DeltaAngle(cmd.Yaw, desired)) < 9f && _sim.Phase != MatchPhase.Ended) cmd.Buttons |= Buttons.Fire;

                    // busy with an objective: keep walking to it while shooting
                    if (selfDefense && Vec2.Dist(dest, _p.Pos) > 1.2f)
                    {
                        Vec2 go = Steer(dest, dt);
                        cmd.MoveX = go.X; cmd.MoveY = go.Y;
                        goto doneMove;
                    }
                    // strafe + keep preferred range
                    _strafeT -= dt;
                    if (_strafeT <= 0) { _strafeDir = _rng.Chance(0.5f) ? 1 : -1; _strafeT = _rng.Range(0.6f, 1.6f); if (_rng.Chance(0.2f)) _wantJump = true; }
                    float pref = Kind == BotKind.Hunter ? 9f : 14f;
                    Vec2 toT = (t.Pos - _p.Pos).Normalized;
                    Vec2 side = new Vec2(toT.Y, -toT.X) * _strafeDir;
                    Vec2 radial = toT * MathUtil.Clamp((d - pref) / 6f, -1f, 1f);
                    Vec2 mv = (side * 0.8f + radial).Normalized;
                    Vec2 probe = _p.Pos + mv * 1.5f;
                    if (!_sim.Map.Nav.Walkable(probe)) mv = -mv;
                    cmd.MoveX = mv.X; cmd.MoveY = mv.Y;
                    doneMove:;
                }
            }

            if (!fighting)
            {
                Vec2 mv = Steer(dest, dt);
                cmd.MoveX = mv.X; cmd.MoveY = mv.Y;
                if (mv.LengthSq > 0.01f)
                {
                    float desired = mv.Yaw;
                    cmd.Yaw = _p.Yaw + MathUtil.Clamp(MathUtil.DeltaAngle(_p.Yaw, desired), -720f * dt, 720f * dt);
                }
                float remaining = Vec2.Dist(dest, _p.Pos);
                if (remaining > 12f && _goal != Goal.HoldZone) cmd.Buttons |= Buttons.Sprint;
                if (_goal == Goal.Flee || _goal == Goal.Center) cmd.Buttons |= Buttons.Sprint;
                if (remaining > 35f && _p.Energy > 75 && _p.DashCd <= 0 && _rng.Chance(0.01f)) _wantDash = true;
            }
            if (!fighting && node.HasValue)
            {
                float desired = (node.Value - _p.Pos).Yaw + _rng.Range(-(1.2f - _skill) * 6f, (1.2f - _skill) * 6f);
                float turn = 540f * dt * (0.6f + _skill);
                cmd.Yaw = _p.Yaw + MathUtil.Clamp(MathUtil.DeltaAngle(_p.Yaw, desired), -turn, turn);
                cmd.Buttons &= ~Buttons.Sprint;
                if (MathF.Abs(MathUtil.DeltaAngle(cmd.Yaw, desired)) < 6f) cmd.Buttons |= Buttons.Fire;
            }

            if (_wantDash) { cmd.Buttons |= Buttons.Dash; _wantDash = false; }
            if (_wantDecoy) { cmd.Buttons |= Buttons.Decoy; _wantDecoy = false; }
            if (_wantPulse) { cmd.Buttons |= Buttons.Pulse; _wantPulse = false; }
            if (_wantJump) { cmd.Buttons |= Buttons.Jump; _wantJump = false; }
            if (_wantBuy > 0) { cmd.Buttons |= _wantBuy == 1 ? Buttons.Buy1 : _wantBuy == 2 ? Buttons.Buy2 : Buttons.Buy3; _wantBuy = 0; }
            return cmd;
        }

        private Vec2 Steer(Vec2 dest, float dt)
        {
            float distToDest = Vec2.Dist(dest, _p.Pos);
            if (distToDest < 0.8f) { _path.Clear(); return Vec2.Zero; }

            _repathT -= dt;
            if (_path.Count == 0 || _repathT <= 0 || Vec2.Dist(_pathGoal, dest) > 4f)
            {
                _sim.Map.Nav.FindPath(_p.Pos, dest, _path);
                _pathIdx = 0;
                _pathGoal = dest;
                _repathT = _rng.Range(2f, 3f);
            }

            // stuck detection
            _stuckT += dt;
            if (_stuckT > 1f)
            {
                if (Vec2.Dist(_lastPos, _p.Pos) < 0.6f)
                {
                    _wantJump = true;
                    _path.Clear();
                    _repathT = 0;
                    if (_goal == Goal.Wander) _goalPos = _sim.Map.Nav.RandomWalkable(_rng);
                }
                _lastPos = _p.Pos;
                _stuckT = 0;
            }

            while (_pathIdx < _path.Count && Vec2.Dist(_path[_pathIdx], _p.Pos) < 1.1f) _pathIdx++;
            Vec2 wp = _pathIdx < _path.Count ? _path[_pathIdx] : dest;
            return (wp - _p.Pos).Normalized;
        }
    }
}
