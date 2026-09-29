namespace Veil.Sim
{
    /// <summary>
    /// Information pillar (GDD §2.1): nobody has perfect information.
    /// Full  = rendered in the world (and on the minimap)
    /// Ping  = only a minimap blip (Tower sight, Reactor exposure, gunfire noise)
    /// </summary>
    public static class Visibility
    {
        public const byte None = 0, Ping = 1, Full = 2;

        /// <summary>Squadmates always see each other; enemies are seen through the eyes of the whole squad.</summary>
        public static byte OfPlayer(MatchSim sim, PlayerState viewer, PlayerState target)
        {
            if (target == viewer) return Full;
            if (!target.Alive) return None;
            if (target.Squad == viewer.Squad) return Full;
            return sim.SquadVisibility(viewer.Squad, target);
        }

        /// <summary>Uncached squad view of an enemy (MatchSim caches this per tick).</summary>
        internal static byte ComputeSquad(MatchSim sim, int squad, PlayerState target)
        {
            byte best = None;
            foreach (var m in sim.Players)
            {
                if (m.Squad != squad) continue;
                if (target.RevealedTo[m.Id] > 0) return Full;
                if (SeesPoint(sim, m, target.Pos, target.ZoneId)) return Full;
                if (m.TowerSightT > 0 || target.PublicPingT > 0) best = Ping;
                else if (target.NoiseT > 0 && Vec2.DistSq(m.Pos, target.Pos) <= GameConfig.FireNoiseRadius * GameConfig.FireNoiseRadius) best = Ping;
            }
            return best;
        }

        public static byte OfDecoy(MatchSim sim, PlayerState viewer, Decoy d)
        {
            var owner = sim.Players[d.Owner];
            if (owner.Squad == viewer.Squad) return Full;
            byte best = None;
            int zone = sim.Map.ZoneAt(d.Pos);
            foreach (var m in sim.Players)
            {
                if (m.Squad != viewer.Squad) continue;
                if (SeesPoint(sim, m, d.Pos, zone)) return Full;
                if (m.TowerSightT > 0) best = Ping;
            }
            return best;
        }

        public static bool SeesPoint(MatchSim sim, PlayerState viewer, Vec2 pos, int targetZone)
        {
            float d2 = Vec2.DistSq(viewer.Pos, pos);
            float vr = GameConfig.VisionRadius;
            if (d2 > vr * vr) return false;
            if (targetZone == sim.RuinsZone)
            {
                float sr = viewer.ZoneId == sim.RuinsZone ? GameConfig.StealthRadius * 1.6f : GameConfig.StealthRadius;
                if (d2 > sr * sr) return false;
            }
            if (d2 < 2.5f * 2.5f) return true;
            return sim.Map.HasLineOfSight(viewer.Pos, pos);
        }
    }
}
