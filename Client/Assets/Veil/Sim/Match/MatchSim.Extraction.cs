using System;

namespace Veil.Sim
{
    /// <summary>
    /// Rilo extraction mode: every squad works through the same chain —
    /// Objective 1 (hack) → Objective 2 (capture) → Objective 3 (collect) → central Vault → Extraction.
    /// Sites are placed by rotating one layout around each squad's spawn, so every squad gets the same distances.
    /// The first Vault completion reveals one extraction point to everyone; only squads that finished their Vault can
    /// extract, any squad can contest. First squad to hold it for ExtractTime wins and the match ends.
    /// </summary>
    public sealed partial class MatchSim
    {
        public static readonly ChainTask[] Chain = { ChainTask.Hack, ChainTask.Capture, ChainTask.Collect, ChainTask.Vault, ChainTask.Extract };

        // per-stage site relative to the squad's spawn direction: (distance from the centre, degrees off the spawn bearing)
        private static readonly (float r, float deg)[] SiteLayout = { (44f, 26f), (30f, -32f), (0f, 0f) };

        public bool ExtractRevealed { get; private set; }
        public Vec2 ExtractPos { get; private set; }
        public int ExtractController { get; private set; } = -1;   // squad holding it alone (-1 none)
        public bool ExtractContested { get; private set; }
        public int WinnerSquad { get; private set; } = -1;

        public static ChainTask TaskOf(int stage) => Chain[Math.Min(stage, Chain.Length - 1)];

        private void StartChain(Vec2[] anchors)
        {
            for (int s = 0; s < Squads.Length; s++)
            {
                var sq = Squads[s];
                sq.Spawn = anchors[s % anchors.Length];
                sq.OwnHome = HomeTerminalFor(sq.Spawn);
                sq.Home = RaidTarget(sq);
                sq.Stage = 0;
                EnterStage(sq);
            }
        }

        public Vec2 CenterTerminal => Map.Zones[TowerZone].Center;

        private Vec2 HomeTerminalFor(Vec2 spawn)
        {
            Vec2 best = spawn * 0.85f; float bd = float.MaxValue;
            foreach (var t in Map.HomeTerminals) { float d = Vec2.DistSq(t, spawn); if (d < bd) { bd = d; best = t; } }
            return best;
        }

        /// <summary>A squad can't hack its own home terminal: it raids an ENEMY home terminal (the nearest one to its
        /// members) or takes the central one. Progress on enemy terminals is one shared value per squad.</summary>
        private Vec2 RaidTarget(SquadState sq)
        {
            Vec2 c = Vec2.Zero; int n = 0;
            foreach (var p in Players) if (p.Squad == sq.Id && Standing(p)) { c += p.Pos; n++; }
            c = n > 0 ? c / n : sq.Spawn;
            Vec2 best = sq.OwnHome; float bd = float.MaxValue;
            foreach (var t in Map.HomeTerminals)
            {
                if (Vec2.DistSq(t, sq.OwnHome) < 1f) continue;
                float d = Vec2.DistSq(t, c);
                if (d < bd) { bd = d; best = t; }
            }
            return best;
        }

        private Vec2 SiteFor(SquadState sq, int stage)
        {
            // Objective 1: the squad's home terminal is the default waypoint (the central one is the risky alternative)
            if (TaskOf(stage) == ChainTask.Hack) return sq.Home;
            // every squad has its OWN Vault inside its own region (never a shared spot where all 16 players pile up):
            // on the spawn bearing, between the spawn and the ring road — 45° away from its capture pad
            if (TaskOf(stage) == ChainTask.Vault)
            {
                Vec2 v = Vec2.FromYaw(sq.Spawn.Yaw) * (Map.Island ? 100f : 30f);
                if (!Map.Nav.Walkable(v)) v = Map.Nav.CellCenter(Map.Nav.NearestWalkable(Map.Nav.CellOf(v)));
                return v;
            }
            if (stage >= SiteLayout.Length) return Vec2.Zero;
            var (r, deg) = SiteLayout[stage];
            if (Map.Island && TaskOf(stage) == ChainTask.Capture) { r = 100f; deg = 45f; }   // island: the neighbouring cardinal region, inside the ring road
            Vec2 c = Vec2.FromYaw(sq.Spawn.Yaw + deg) * r;
            if (!Map.Nav.Walkable(c)) c = Map.Nav.CellCenter(Map.Nav.NearestWalkable(Map.Nav.CellOf(c)));
            return c;
        }

        // ------------------------------------------------------------------ Hack Terminal

        /// <summary>Objective 1 runs on two terminals at once: the squad's HOME terminal (slow, quiet) and the shared CENTRAL one
        /// (fast, contested, bonus). Finishing either completes the objective.</summary>
        private void UpdateHack(SquadState sq, float dt)
        {
            // re-pick the enemy terminal to raid only while nobody is mid-hack there
            if (sq.HomeHacker < 0 && sq.HomeProg <= 0f) { sq.Home = RaidTarget(sq); sq.Site = sq.Home; }
            // bots "solve the puzzle" only while standing in a terminal ring
            float hr2 = GameConfig.HomeHackRadius * GameConfig.HomeHackRadius, cr2 = GameConfig.HackRadius * GameConfig.HackRadius;
            foreach (var p in Players)
                if (p.IsBot && p.Squad == sq.Id && Vec2.DistSq(p.Pos, sq.Home) > hr2 && Vec2.DistSq(p.Pos, CenterTerminal) > cr2) p.BotHackT = 0;
            UpdateTerminal(sq, dt, true);
            UpdateTerminal(sq, dt, false);
            if (sq.CenterProg >= 1f)
            {
                // central bonus: every enemy revealed to the squad + energy
                foreach (var p in Players)
                {
                    if (p.Squad != sq.Id) continue;
                    p.Energy = MathF.Min(GameConfig.MaxEnergy, p.Energy + GameConfig.CenterBonusEnergy);
                    foreach (var o in Players) if (o.Squad != sq.Id) o.RevealedTo[p.Id] = MathF.Max(o.RevealedTo[p.Id], GameConfig.CenterBonusReveal);
                }
                Events.Add(new SimEvent(EventType.CenterBonus, sq.Id, 0, 0, CenterTerminal));
                sq.StageProg = 1f;
            }
            else sq.StageProg = MathF.Max(sq.HomeProg, sq.CenterProg);
        }

        /// <summary>One hacker per squad per terminal. Press HACK (after the circuit puzzle) inside the ring to start or take over;
        /// teammates don't speed it up, they defend. The hacker leaving / dying, or any enemy in the ring, pauses it —
        /// progress is never lost.</summary>
        private void UpdateTerminal(SquadState sq, float dt, bool home)
        {
            Vec2 at = home ? sq.Home : CenterTerminal;
            float radius = home ? GameConfig.HomeHackRadius : GameConfig.HackRadius;
            float r2 = radius * radius;
            bool enemy = false;
            foreach (var p in Players)
                if (Standing(p) && p.Squad != sq.Id && Vec2.DistSq(p.Pos, at) <= r2) enemy = true;

            int hacker = home ? sq.HomeHacker : sq.CenterHacker;
            var hp = hacker >= 0 ? Players[hacker] : null;
            if (hp != null && (!Standing(hp) || Vec2.DistSq(hp.Pos, at) > r2)) hacker = -1;   // left / down: paused
            // start / take over: a squadmate in the ring presses HACK (bots after their "puzzle" time)
            if (hacker < 0)
                foreach (var p in Players)
                {
                    if (p.Squad != sq.Id || !Standing(p) || Vec2.DistSq(p.Pos, at) > r2) continue;
                    if (p.IsBot && !enemy) { p.BotHackT += dt; if (p.BotHackT >= GameConfig.BotPuzzleTime) p.HackRequest = true; }
                    if (!p.HackRequest) continue;
                    hacker = p.Id; p.BotHackT = 0;
                    Events.Add(new SimEvent(EventType.HackActivity, sq.Id, home ? 1 : 0, (int)((home ? sq.HomeProg : sq.CenterProg) * 100), at));
                    if (home) AlertHomeOwner(sq, at, (int)(sq.HomeProg * 100));
                    break;
                }
            bool contested = enemy && hacker >= 0;
            bool was = home ? sq.HomeContested : sq.Contested;
            if (home) { sq.HomeHacker = hacker; sq.HomeHackers = hacker >= 0 ? 1 : 0; sq.HomeContested = contested; }
            else { sq.CenterHacker = hacker; sq.Hackers = hacker >= 0 ? 1 : 0; sq.Contested = contested; }
            if (contested && !was) Events.Add(new SimEvent(EventType.HackContested, sq.Id, home ? 1 : 0, 0, at));
            if (hacker < 0 || enemy) return;

            float time = home ? GameConfig.HackTime : GameConfig.CenterHackTime;
            float before = home ? sq.HomeProg : sq.CenterProg;
            float prog = MathF.Min(1f, before + dt / time * ProgressMult(sq));
            if (home) sq.HomeProg = prog; else sq.CenterProg = prog;
            // pressure: enemies hear about it every 25 %
            int step = (int)(prog * 4f);
            ref int alerted = ref (home ? ref sq.HomeAlertPct : ref sq.CenterAlertPct);
            if (step > alerted && step < 4)
            {
                alerted = step;
                Events.Add(new SimEvent(EventType.HackActivity, sq.Id, home ? 1 : 0, step * 25, at));
                if (home) AlertHomeOwner(sq, at, step * 25);
            }
        }

        private void UpdateNodes(SquadState sq, float dt)
        {
            float s2 = GameConfig.NodeStandRadius * GameConfig.NodeStandRadius;
            for (int i = sq.Nodes.Count - 1; i >= 0; i--)
            {
                var n = sq.Nodes[i];
                if (n.Kind == NodeKind.Destroy) continue;   // projectiles handle it
                bool mine = false, enemy = false;
                foreach (var p in Players)
                {
                    if (!Standing(p) || Vec2.DistSq(p.Pos, n.Pos) > s2) continue;
                    if (p.Squad == sq.Id) mine = true; else enemy = true;
                }
                n.Contested = mine && enemy;
                float time = n.Kind == NodeKind.Stabilize ? GameConfig.StabilizeTime : GameConfig.OverrideTime;
                if (mine && !enemy) n.Prog += dt / time;
                else if (!mine && n.Kind == NodeKind.Override) n.Prog = MathF.Max(0, n.Prog - dt / time * 0.5f);
                if (n.Prog >= 1f) ResolveNode(sq, i, -1);
            }
        }

        internal void ResolveNode(SquadState sq, int index, int by)
        {
            var n = sq.Nodes[index];
            sq.Nodes.RemoveAt(index);
            Events.Add(new SimEvent(EventType.NodeDestroyed, by >= 0 ? by : -1 - sq.Id, sq.Nodes.Count, (int)n.Kind, n.Pos));
        }

        /// <summary>Damage a Destroy node (called by projectiles). Returns true when the bolt was absorbed.</summary>
        internal bool HitNode(PlayerState shooter, Vec2 at)
        {
            var sq = Squads[shooter.Squad];
            float r2 = GameConfig.HackNodeRadius * GameConfig.HackNodeRadius;
            for (int i = 0; i < sq.Nodes.Count; i++)
            {
                var n = sq.Nodes[i];
                if (n.Kind != NodeKind.Destroy || Vec2.DistSq(n.Pos, at) > r2) continue;
                n.Hp--;
                n.Prog = 1f - n.Hp / (float)GameConfig.HackNodeHp;
                if (n.Hp <= 0) ResolveNode(sq, i, shooter.Id);
                return true;
            }
            return false;
        }

        /// <summary>Instability: one Destroy, one Stabilize and one Override node. The first wave sits inside the plaza
        /// (between the low walls and the pillars), the second spreads to the moat bridges — the squad has to split up.</summary>
        /// <summary>Instability: one Destroy, one Stabilize and one Override node. First wave close to the terminal, second wave
        /// further out (centre: past the moat bridges) — the squad has to split up.</summary>
        private void SpawnInstability(SquadState sq, bool home, int wave)
        {
            Vec2 at = home ? sq.Home : CenterTerminal;
            sq.NodesHome = home;
            bool outer = wave >= 2;
            var kinds = new[] { NodeKind.Destroy, NodeKind.Stabilize, NodeKind.Override };
            for (int i = kinds.Length - 1; i > 0; i--) { int j = Rng.Int(i + 1); (kinds[i], kinds[j]) = (kinds[j], kinds[i]); }
            float baseYaw = outer && !home ? Rng.Int(4) * 90f : Rng.Range(0, 360);
            for (int i = 0; i < GameConfig.HackNodes; i++)
            {
                float dist = home ? (outer ? Rng.Range(13f, 17f) : Rng.Range(8f, 11f)) : (outer ? Rng.Range(23f, 27f) : Rng.Range(9f, 12f));
                float yaw = outer && !home ? baseYaw + i * 90f + Rng.Range(-6f, 6f) : baseYaw + i * 120f + Rng.Range(-20f, 20f);
                Vec2 c = at + Vec2.FromYaw(yaw) * dist;
                if (!Map.Nav.Walkable(c)) c = Map.Nav.CellCenter(Map.Nav.NearestWalkable(Map.Nav.CellOf(c)));
                sq.Nodes.Add(new HackNode { Pos = c, Kind = kinds[i] });
            }
            Events.Add(new SimEvent(EventType.HackGlitch, sq.Id, wave, home ? 1 : 0, at));
        }

        private void EnterStage(SquadState sq)
        {
            sq.Nodes.Clear();
            sq.Glitches = 0; sq.Hackers = 0; sq.Contested = false;
            sq.HomeProg = 0; sq.CenterProg = 0; sq.HomeHacker = -1; sq.CenterHacker = -1; sq.HomeAlertPct = 0; sq.CenterAlertPct = 0; sq.HomeGlitches = 0; sq.HomeHackers = 0; sq.HomeContested = false; sq.NodesHome = false;
            sq.StageProg = 0;
            sq.Site = SiteFor(sq, sq.Stage);
            sq.CoresAtStart = SquadCores(sq.Id);
        }

        private int SquadCores(int squad)
        {
            int n = 0;
            foreach (var p in Players) if (p.Squad == squad) n += p.CoresCollected;
            return n;
        }

        private bool Standing(PlayerState p) => p.Alive && !p.Downed;

        private void UpdateChain(float dt)
        {
            if (!GameConfig.ExtractionMode || Ended) return;
            foreach (var sq in Squads)
            {
                if (sq.Stage >= 4) continue;
                var task = TaskOf(sq.Stage);
                if (task == ChainTask.Collect)
                {
                    sq.StageProg = MathUtil.Clamp01((SquadCores(sq.Id) - sq.CoresAtStart) / (float)GameConfig.CollectCores);
                }
                else if (task == ChainTask.Hack) UpdateHack(sq, dt);
                else
                {
                    float radius = task == ChainTask.Capture ? GameConfig.CaptureRadius : GameConfig.VaultRadius;
                    float time = task == ChainTask.Capture ? GameConfig.PadCaptureTime : GameConfig.VaultTime;
                    int mine = 0; bool enemy = false;
                    float r2 = radius * radius, e2 = (radius + 2f) * (radius + 2f);
                    foreach (var p in Players)
                    {
                        if (!Standing(p)) continue;
                        float d2 = Vec2.DistSq(p.Pos, sq.Site);
                        if (p.Squad == sq.Id) { if (d2 <= r2) mine++; }
                        else if (d2 <= e2) enemy = true;
                    }
                    if (mine > 0 && !enemy)
                    {
                        float rate = task == ChainTask.Capture ? 1f + 0.25f * (mine - 1) : 1f;
                        sq.StageProg = MathF.Min(1f, sq.StageProg + dt / time * rate * ProgressMult(sq));
                    }
                }
                if (sq.StageProg >= 1f) CompleteStage(sq);
            }
            UpdateExtraction(dt);
        }

        private void CompleteStage(SquadState sq)
        {
            int done = sq.Stage;
            foreach (var p in Players)
            {
                if (p.Squad != sq.Id) continue;
                p.Score.Squad += GameConfig.StagePoints;
                // completing a stage gives the squad a short look at enemies around the site
                float r2 = GameConfig.StageRevealRadius * GameConfig.StageRevealRadius;
                foreach (var o in Players)
                    if (o.Squad != sq.Id && o.Alive && Vec2.DistSq(o.Pos, sq.Site) <= r2) o.RevealedTo[p.Id] = GameConfig.StageRevealTime;
            }
            Events.Add(new SimEvent(EventType.StageComplete, sq.Id, done, 0, sq.Site));
            sq.Stage++;
            if (sq.Stage <= 3) EnterStage(sq);
            else
            {
                sq.StageProg = 1f;
                sq.Site = ExtractRevealed ? ExtractPos : sq.Site;
                if (!ExtractRevealed) RevealExtraction(sq);
            }
        }

        public float ExtractLockT { get; private set; }      // > 0: revealed but not open yet
        public float ExtractSecure { get; private set; }     // 0..1 the controller securing the zone
        public bool ExtractFinal { get; private set; }       // controller is in the last 20% — final phase
        private float _extractPingT;
        private readonly System.Collections.Generic.List<Vec2> _pathTmp = new System.Collections.Generic.List<Vec2>();

        /// <summary>Where a squad is right now: the average of its living members (its spawn if all are down).</summary>
        private Vec2 SquadCentroid(SquadState sq)
        {
            Vec2 sum = Vec2.Zero; int n = 0;
            foreach (var p in Players) if (p.Squad == sq.Id && p.Alive) { sum += p.Pos; n++; }
            return n > 0 ? sum * (1f / n) : sq.Spawn;
        }

        private float PathLength(Vec2 from, Vec2 to)
        {
            if (!Map.Nav.FindPath(from, to, _pathTmp, 20000)) return -1f;
            float len = 0; Vec2 prev = from;
            foreach (var w in _pathTmp) { len += Vec2.Dist(prev, w); prev = w; }
            return len;
        }

        /// <summary>Open ground (the zone itself) with some cover around it (to fight over), 0 = unusable.</summary>
        private float SiteQuality(Vec2 c)
        {
            int open = 0, total = 0;
            for (int k = 0; k < 16; k++)
                foreach (float rr in new[] { 2.5f, 5f, GameConfig.ExtractRadius })
                { total++; if (Map.Nav.Walkable(c + Vec2.FromYaw(k * 22.5f) * rr)) open++; }
            if (open < total * 0.85f) return 0f;
            int cover = 0;
            foreach (var o in Map.Obstacles)
            {
                if (!o.BlocksShots || o.Kind == ObstacleKind.Water) continue;
                float d = Vec2.Dist(o.Center, c) - o.BoundRadius;
                if (d > GameConfig.ExtractRadius + 1f && d < 26f) cover++;
            }
            return 1f + MathF.Min(cover, 10);
        }

        /// <summary>Fairness: every squad about the same distance away, and the Vault opener never the closest.</summary>
        private static float Fairness(float[] d, int opener)
        {
            float min = float.MaxValue, max = 0, others = 0; int n = 0;
            for (int i = 0; i < d.Length; i++)
            {
                min = MathF.Min(min, d[i]); max = MathF.Max(max, d[i]);
                if (i != opener) { others += d[i]; n++; }
            }
            others /= MathF.Max(1, n);
            return -(max - min) - MathF.Max(0, others - d[opener]) * 2f;
        }

        /// <summary>Pick the extraction point from where the squads ARE (not where they spawned): candidates on a ring
        /// around the centre, open ground with cover nearby, reachable by everyone, about equally far (walking) from every
        /// squad, and never handed to the squad that opened the Vault.</summary>
        private void RevealExtraction(SquadState first)
        {
            int ns = Squads.Length;
            var at = new Vec2[ns];
            for (int i = 0; i < ns; i++) at[i] = SquadCentroid(Squads[i]);
            var cands = new System.Collections.Generic.List<(Vec2 c, float q, float score)>();
            var d = new float[ns];
            for (int b = 0; b < 24; b++)
                foreach (float rf in new[] { 0.8f, 0.95f, 1.08f })
                {
                    Vec2 c = Vec2.FromYaw(b * 15f) * (GameConfig.ExtractDistance * rf);
                    float q = SiteQuality(c);
                    if (q <= 0) continue;
                    for (int i = 0; i < ns; i++) d[i] = Vec2.Dist(at[i], c);
                    cands.Add((c, q, Fairness(d, first.Id) + q * 3f + Rng.Range(0, 3f)));
                }
            cands.Sort((x, y) => y.score.CompareTo(x.score));
            Vec2 best = Vec2.Zero; float bestScore = float.MinValue;
            for (int k = 0; k < cands.Count && k < 6; k++)
            {
                var (c, q, _) = cands[k];
                bool ok = true;
                for (int i = 0; i < ns && ok; i++) { d[i] = PathLength(at[i], c); ok = d[i] >= 0; }
                if (!ok) continue;
                float score = Fairness(d, first.Id) + q * 3f;
                if (score > bestScore) { bestScore = score; best = c; }
            }
            if (bestScore == float.MinValue)   // nothing passed: old rule, between two spawns
            {
                float a = Squads[0].Spawn.Yaw, b2 = Squads[1 % ns].Spawn.Yaw;
                best = Vec2.FromYaw(a + MathUtil.DeltaAngle(a, b2) * 0.5f) * GameConfig.ExtractDistance;
                if (!Map.Nav.Walkable(best)) best = Map.Nav.CellCenter(Map.Nav.NearestWalkable(Map.Nav.CellOf(best)));
            }
            ExtractPos = best;
            ExtractRevealed = true;
            ExtractLockT = GameConfig.ExtractUnlockDelay;
            foreach (var sq in Squads) if (sq.Stage >= 4) sq.Site = ExtractPos;
            Events.Add(new SimEvent(EventType.ExtractRevealed, first.Id, 0, (int)GameConfig.ExtractUnlockDelay, ExtractPos));
        }

        /// <summary>Test only: every squad has opened its Vault, extraction is open and <paramref name="squad"/> stands in it at 82%.</summary>
        public void DebugExtraction(int squad)
        {
            foreach (var sq in Squads) { sq.Stage = 4; sq.StageProg = 1f; }
            if (!ExtractRevealed) RevealExtraction(Squads[squad]);
            ExtractLockT = 0f;
            Squads[squad].ExtractProg = 0.82f;
            Squads[squad].ExtractAlertPct = 75;
            foreach (var p in Players)
                if (p.Squad == squad && p.Alive) p.Pos = ExtractPos + Vec2.FromYaw(p.Id * 90f) * 2f;
                else if (p.Alive && Vec2.Dist(p.Pos, ExtractPos) < GameConfig.ExtractRadius + 4f) p.Pos = ExtractPos + Vec2.FromYaw(p.Id * 40f) * 30f;
        }

        private void UpdateExtraction(float dt)
        {
            if (!ExtractRevealed) return;
            if (ExtractLockT > 0)
            {
                ExtractLockT = MathF.Max(0, ExtractLockT - dt);
                if (ExtractLockT <= 0) Events.Add(new SimEvent(EventType.ExtractOpen, -1, 0, 0, ExtractPos));
                return;
            }
            int present = -1; bool many = false;
            float r2 = GameConfig.ExtractRadius * GameConfig.ExtractRadius;
            foreach (var p in Players)
            {
                if (!Standing(p) || Vec2.DistSq(p.Pos, ExtractPos) > r2) continue;
                if (present < 0) present = p.Squad;
                else if (present != p.Squad) many = true;
            }
            int controller = many ? -1 : present;
            ExtractContested = many;
            if (controller != ExtractController)
            {
                ExtractController = controller;
                ExtractSecure = 0f;     // a new holder (or a contest) always starts securing from zero
                Events.Add(new SimEvent(EventType.ExtractControl, controller, many ? 1 : 0, 0, ExtractPos));
            }
            bool final = false;
            if (controller >= 0 && CanExtract(Squads[controller]))
            {
                var sq = Squads[controller];
                if (ExtractSecure < 1f) ExtractSecure = MathF.Min(1f, ExtractSecure + dt / GameConfig.ExtractSecureTime);
                else
                {
                    sq.ExtractProg = MathF.Min(1f, sq.ExtractProg + dt / GameConfig.ExtractTime);
                    int pct = (int)(sq.ExtractProg * 4) * 25;
                    if (pct > sq.ExtractAlertPct && pct < 100) { sq.ExtractAlertPct = pct; Events.Add(new SimEvent(EventType.ExtractAlert, sq.Id, 0, pct, ExtractPos)); }
                    // holders are pinged to everyone — and in the final phase they are visible all the time
                    final = sq.ExtractProg >= GameConfig.ExtractFinalAt;
                    _extractPingT -= dt;
                    if (final || _extractPingT <= 0)
                    {
                        _extractPingT = GameConfig.ExtractPingEvery;
                        foreach (var h in Players)
                        {
                            if (h.Squad != sq.Id || !Standing(h) || Vec2.DistSq(h.Pos, ExtractPos) > r2) continue;
                            foreach (var v in Players) if (v.Squad != sq.Id) v.RevealedTo[h.Id] = MathF.Max(v.RevealedTo[h.Id], final ? 0.6f : 2.5f);
                        }
                    }
                    if (sq.ExtractProg >= 1f) { WinnerSquad = sq.Id; EndMatch(); }
                }
            }
            else ExtractSecure = 0f;
            if (final && !ExtractFinal) Events.Add(new SimEvent(EventType.ExtractFinal, controller, 0, 0, ExtractPos));
            ExtractFinal = final;
        }

        /// <summary>Ranking key for extraction mode: winner, then extraction progress, then chain stage, then score.</summary>
        private float ChainRankKey(SquadState sq)
        {
            if (sq.Id == WinnerSquad) return 1e9f;
            return sq.ExtractProg * 1e6f + (sq.Stage + sq.StageProg) * 1e4f + sq.Total;
        }
    }
}
