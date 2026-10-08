using System.Collections.Generic;

namespace Veil.Sim
{
    /// <summary>
    /// Kinematics shared by the authoritative simulation and client-side prediction.
    /// Anything that changes Pos/Vel/H/Dash must live here so both sides agree.
    /// </summary>
    public static class Movement
    {
        public static void Step(PlayerState p, in InputCmd cmd, MapData map, float dt, List<SimEvent> events = null)
        {
            if (!p.Alive) return;
            p.Yaw = cmd.Yaw;
            if (p.Downed)
            {
                // crawl: no dash, sprint or jump
                p.DashT = 0;
                p.Vel = Vec2.MoveTowards(p.Vel, cmd.Move * GameConfig.DownedSpeed, GameConfig.GroundAccel * dt);
                p.Pos += (p.Vel + p.Knock) * dt;
                p.Knock = Vec2.MoveTowards(p.Knock, Vec2.Zero, GameConfig.KnockbackDecay * dt);
                p.H = 0; p.VH = 0; p.Grounded = true;
                map.ResolveCircle(ref p.Pos, GameConfig.PlayerRadius, 0);
                return;
            }

            // ---- Dash (Q) ----
            if (p.DashCd > 0) p.DashCd = System.MathF.Max(0, p.DashCd - dt);
            if (cmd.Has(Buttons.Dash) && p.DashCd <= 0 && p.DashT <= 0 && p.Energy >= GameConfig.DashCost)
            {
                Vec2 m = cmd.Move;
                p.DashDir = m.LengthSq > 0.01f ? m.Normalized : Vec2.FromYaw(cmd.Yaw);
                p.DashT = GameConfig.DashDuration;
                p.DashCd = GameConfig.DashCooldown;
                p.Energy -= GameConfig.DashCost;
                p.CastSeq++;
                events?.Add(new SimEvent(EventType.DashStart, p.Id, 0, 0, p.Pos));
            }

            float speedMul = (p.SpeedBuffT > 0 ? GameConfig.SpeedBuffMult : 1f) * (p.Fists ? GameConfig.FistsSpeedMult : 1f);
            if (p.SpeedBuffT > 0) p.SpeedBuffT = System.MathF.Max(0, p.SpeedBuffT - dt);

            Vec2 move = cmd.Move;
            bool sprint = cmd.Has(Buttons.Sprint) && move.LengthSq > 0.04f && !cmd.Has(Buttons.Fire);
            float speed = (sprint ? GameConfig.SprintSpeed : GameConfig.RunSpeed) * speedMul;
            Vec2 desired = move * speed;

            if (p.DashT > 0)
            {
                p.Vel = p.DashDir * GameConfig.DashSpeed;
                p.DashT -= dt;
                if (p.DashT <= 0) p.Vel = p.DashDir * speed;
            }
            else if (p.LaunchT > 0)
            {
                // flying off a jump pad: keep the launch velocity, a little steering
                p.LaunchT -= dt;
                p.Vel = Vec2.MoveTowards(p.Vel, p.Vel.Normalized * GameConfig.PadSpeed + desired * 0.25f, GameConfig.AirAccel * 0.3f * dt);
            }
            else
            {
                float accel;
                if (!p.Grounded) accel = GameConfig.AirAccel;
                else if (desired.LengthSq < 0.01f) accel = GameConfig.GroundDecel;
                else if (Vec2.Dot(p.Vel, desired) < 0) accel = GameConfig.GroundTurnAccel;
                else accel = GameConfig.GroundAccel;
                p.Vel = Vec2.MoveTowards(p.Vel, desired, accel * dt);
            }

            // ---- Jump pad ----
            if (p.Grounded && p.DashT <= 0)
            {
                int pad = map.PadAt(p.Pos);
                if (pad >= 0)
                {
                    var jp = map.JumpPads[pad];
                    p.VH = GameConfig.PadUpVelocity;
                    p.Vel = jp.Dir * GameConfig.PadSpeed;
                    p.Grounded = false;
                    p.LaunchT = 2.5f;
                    p.JumpSeq++;
                    events?.Add(new SimEvent(EventType.DashStart, p.Id, 1, 0, p.Pos));
                }
            }

            // ---- Jump / mantle (running into low cover vaults over it) ----
            if (cmd.Has(Buttons.Jump) && p.Grounded)
            {
                p.VH = GameConfig.JumpVelocity;
                p.Grounded = false;
                p.JumpSeq++;
            }
            else if (p.Grounded && move.LengthSq > 0.25f && map.CanMantle(p.Pos, move.Normalized, GameConfig.PlayerRadius))
            {
                p.VH = GameConfig.MantleVelocity;
                p.Grounded = false;
                p.JumpSeq++;
            }
            if (!p.Grounded)
            {
                float g = GameConfig.Gravity;
                bool held = cmd.Has(Buttons.Jump) || p.LaunchT > 0;
                if (p.VH < 0) g *= GameConfig.FallGravityMult;
                else if (!held) g *= GameConfig.ShortHopGravityMult;
                p.VH -= g * dt;
                p.H += p.VH * dt;
                if (p.H <= 0)
                {
                    p.H = 0;
                    p.VH = 0;
                    p.Grounded = true;
                    p.LaunchT = 0;
                    events?.Add(new SimEvent(EventType.Land, p.Id, 0, 0, p.Pos));
                }
            }

            // ---- Integrate + collide ----
            Vec2 np = p.Pos + (p.Vel + p.Knock) * dt;
            if (map.ResolveCircle(ref np, GameConfig.PlayerRadius, p.H))
            {
                // keep sliding velocity only (removes the component pushing into the wall)
                Vec2 actual = (np - p.Pos) / dt - p.Knock;
                p.Vel = actual.ClampLength(p.Vel.Length);
            }
            p.Pos = np;
            p.Knock = Vec2.MoveTowards(p.Knock, Vec2.Zero, GameConfig.KnockbackDecay * dt);
        }
    }
}
