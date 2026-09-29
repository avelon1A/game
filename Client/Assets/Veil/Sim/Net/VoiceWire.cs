using System.Collections.Generic;

namespace Veil.Sim
{
    /// <summary>
    /// Voice relay wire format (raw UDP, separate port from the game). Opus frames are opaque bytes to the server.
    /// client → server: Join [channel, profileId, token] · Frame [seq u16, opus...] · Leave
    /// server → client: Joined [slot u8] · Roster [n, (slot u8, profileId)…] · Frame [slot u8, seq u16, opus...] · Denied [reason]
    /// Joining again every ~2 s doubles as a keep-alive; endpoints silent for 6 s are dropped.
    /// </summary>
    public static class VoiceWire
    {
        public const byte Join = 1, Frame = 2, Leave = 3;
        public const byte Joined = 0x81, Roster = 0x82, FrameOut = 0x83, Denied = 0x84;

        public const int SampleRate = 16000;
        public const int FrameMs = 20;
        public const int FrameSamples = SampleRate * FrameMs / 1000;   // 320
        public const int MaxPacket = 600;

        public static void WriteJoin(ByteWriter w, string channel, string profileId, string token)
        {
            w.Reset(); w.U8(Join); w.Str(channel); w.Str(profileId); w.Str(token);
        }

        public static void WriteRoster(ByteWriter w, IList<(byte slot, string id)> members)
        {
            w.Reset(); w.U8(Roster); w.U8((byte)members.Count);
            foreach (var m in members) { w.U8(m.slot); w.Str(m.id); }
        }

        public static List<(byte slot, string id)> ReadRoster(ByteReader r)
        {
            var list = new List<(byte, string)>();
            int n = r.U8();
            for (int i = 0; i < n; i++) list.Add((r.U8(), r.Str()));
            return list;
        }
    }
}
