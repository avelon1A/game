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
                sq.Home = HomeTerminalFor(sq.Spawn);
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

        private Vec2 SiteFor(SquadState sq, int stage)
        {
            // Objective 1: the squad's home terminal is the default waypoint (the central one is the risky alternative)
            if (TaskOf(stage) == ChainTask.Hack) return sq.Home;
            // the Vault is shared: the central plaza (cover, low walls, pillars, bridges)
            if (TaskOf(stage) == ChainTask.Vault) return CenterTerminal;
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

        private void UpdateTerminal(SquadState sq, float dt, bool home)
        {
            Vec2 at = home ? sq.Home : CenterTerminal;
            float radius = home ? GameConfig.HomeHackRadius : GameConfig.HackRadius;
            float r2 = radius * radius;
            int mine = 0; bool enemy = false;
            foreach (var p in Players)
            {
                if (!Standing(p) || Vec2.DistSq(p.Pos, at) > r2) continue;
                if (p.Squad == sq.Id) mine++; else enemy = true;
            }
            bool contested = enemy && mine > 0;
            bool was = home ? sq.HomeContested : sq.Contested;
            if (home) { sq.HomeHackers = mine; sq.HomeContested = contested; } else { sq.Hackers = mine; sq.Contested = contested; }
            if (contested && !was) Events.Add(new SimEvent(EventType.HackContested, sq.Id, home ? 1 : 0, 0, at));

            if (sq.Nodes.Count > 0 && sq.NodesHome == home) { UpdateNodes(sq, dt); return; }   // this terminal is unstable
            if (mine == 0 || enemy) return;                                                     // contest pauses, never removes

            if (!home)
            {
                // the central terminal is public: starting it is an information event for everyone else
                if (Time - sq.LastActivity > GameConfig.HackActivityCooldown) Events.Add(new SimEvent(EventType.HackActivity, sq.Id, 0, 0, at));
                sq.LastActivity = Time;
            }

            float rate = GameConfig.HackSpeed[Math.Min(mine, GameConfig.HackSpeed.Length - 1)];
            float time = home ? GameConfig.HackTime : GameConfig.CenterHackTime;
            var marks = home ? GameConfig.HackInstability : GameConfig.CenterInstability;
            int glitches = home ? sq.HomeGlitches : sq.Glitches;
            float prog = (home ? sq.HomeProg : sq.CenterProg) + dt / time * rate;
            if (glitches < marks.Length && prog >= marks[glitches])
            {
                prog = marks[glitches];
                if (sq.Nodes.Count == 0)
                {
                    if (home) sq.HomeGlitches++; else sq.Glitches++;
                    SpawnInstability(sq, home, home ? sq.HomeGlitches : sq.Glitches);
                }
            }
            prog = MathF.Min(1f, prog);
            if (home) sq.HomeProg = prog; else sq.CenterProg = prog;
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
            sq.HomeProg = 0; sq.CenterProg = 0; sq.HomeGlitches = 0; sq.HomeHackers = 0; sq.HomeContested = false; sq.NodesHome = false;
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
                        sq.StageProg = MathF.Min(1f, sq.StageProg + dt / time * rate);
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

        private void RevealExtraction(SquadState first)
        {
            // candidates: midway between neighbouring squads' spawn bearings — equally far from two squads each
            Vec2 best = Vec2.Zero; float bestScore = float.MinValue;
            for (int i = 0; i < Squads.Length; i++)
            {
                float a = Squads[i].Spawn.Yaw, b = Squads[(i + 1) % Squads.Length].Spawn.Yaw;
                float mid = a + MathUtil.DeltaAngle(a, b) * 0.5f;
                Vec2 c = Vec2.FromYaw(mid) * GameConfig.ExtractDistance;
                if (!Map.Nav.Walkable(c)) c = Map.Nav.CellCenter(Map.Nav.NearestWalkable(Map.Nav.CellOf(c)));
                // never hand it to the squad that opened the Vault: prefer the point farthest from them
                float score = Vec2.Dist(c, first.Spawn) + Rng.Range(0, 4f);
                if (score > bestScore) { bestScore = score; best = c; }
            }
            ExtractPos = best;
            ExtractRevealed = true;
            foreach (var sq in Squads) if (sq.Stage >= 4) sq.Site = ExtractPos;
            Events.Add(new SimEvent(EventType.ExtractRevealed, first.Id, 0, 0, ExtractPos));
        }

        private void UpdateExtraction(float dt)
        {
            if (!ExtractRevealed) return;
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
                Events.Add(new SimEvent(EventType.ExtractControl, controller, many ? 1 : 0, 0, ExtractPos));
            }
            if (controller < 0) return;
            var sq = Squads[controller];
            if (!sq.VaultDone) return;     // only squads that finished their Vault can extract
            sq.ExtractProg = MathF.Min(1f, sq.ExtractProg + dt / GameConfig.ExtractTime);
            if (sq.ExtractProg >= 1f)
            {
                WinnerSquad = sq.Id;
                EndMatch();
            }
        }

        /// <summary>Ranking key for extraction mode: winner, then extraction progress, then chain stage, then score.</summary>
        private float ChainRankKey(SquadState sq)
        {
            if (sq.Id == WinnerSquad) return 1e9f;
            return sq.ExtractProg * 1e6f + (sq.Stage + sq.StageProg) * 1e4f + sq.Total;
        }
    }
}
