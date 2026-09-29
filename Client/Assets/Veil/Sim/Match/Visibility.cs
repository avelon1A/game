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

        public static byte OfPlayer(MatchSim sim, PlayerState viewer, PlayerState target)
        {
            if (target == viewer) return Full;
            if (!target.Alive) return None;
            if (target.RevealedTo[viewer.Id] > 0) return Full;
            if (SeesPoint(sim, viewer, target.Pos, target.ZoneId)) return Full;
            if (viewer.TowerSightT > 0 || target.PublicPingT > 0) return Ping;
            if (target.NoiseT > 0 && Vec2.DistSq(viewer.Pos, target.Pos) <= GameConfig.FireNoiseRadius * GameConfig.FireNoiseRadius) return Ping;
            return None;
        }

        public static byte OfDecoy(MatchSim sim, PlayerState viewer, Decoy d)
        {
            if (d.Owner == viewer.Id) return Full;
            if (SeesPoint(sim, viewer, d.Pos, sim.Map.ZoneAt(d.Pos))) return Full;
            if (viewer.TowerSightT > 0) return Ping;
            return None;
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
