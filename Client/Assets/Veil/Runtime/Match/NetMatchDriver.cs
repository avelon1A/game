using System;
using System.Collections.Generic;
using Veil.Net;
using Veil.Sim;

namespace Veil.Match
{
    /// <summary>Online play: inputs go to the authoritative server, snapshots/events come back.</summary>
    public sealed class NetMatchDriver : IMatchDriver
    {
        private readonly VeilNetClient _net;
        private readonly List<InputCmd> _recent = new List<InputCmd>();

        public int LocalPlayerId { get; }
        public List<RosterEntry> Roster { get; }
        public int MatchSeconds { get; }
        public bool Autopilot => false;
        public bool IsOnline => true;
        public Action<Snapshot> OnSnapshot { get; set; }
        public Action<List<SimEvent>> OnEvents { get; set; }
        public Action<List<PlayerResult>> OnEnd { get; set; }

        public NetMatchDriver(VeilNetClient net, MatchStartMsg start)
        {
            _net = net;
            LocalPlayerId = start.YourPlayerId;
            Roster = start.Roster;
            MatchSeconds = start.MatchSeconds;
            _net.SnapshotReceived += HandleSnapshot;
            _net.EventsReceived += HandleEvents;
            _net.MatchEnded += HandleEnd;
        }

        private void HandleSnapshot(Snapshot s) => OnSnapshot?.Invoke(s);
        private void HandleEvents(List<SimEvent> e) => OnEvents?.Invoke(e);
        private void HandleEnd(List<PlayerResult> r) => OnEnd?.Invoke(r);

        public void SendInput(InputCmd cmd)
        {
            _recent.Add(cmd);
            if (_recent.Count > 3) _recent.RemoveAt(0);
            _net.SendInputs(_recent, 0, _recent.Count);
        }

        public void Poll() => _net.Poll();

        public void DebugSkip(float seconds) { }

        public void Dispose()
        {
            _net.SnapshotReceived -= HandleSnapshot;
            _net.EventsReceived -= HandleEvents;
            _net.MatchEnded -= HandleEnd;
        }
    }
}
