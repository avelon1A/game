using System;
using System.Collections.Generic;
using Veil.Sim;

namespace Veil.Match
{
    /// <summary>
    /// Offline play: runs the authoritative <see cref="MatchSim"/> in-process with bots, and feeds
    /// the client exactly what a server would (per-viewer snapshots and filtered events).
    /// </summary>
    public sealed class LocalMatchDriver : IMatchDriver
    {
        public readonly MatchSim Sim;
        private readonly PlayerState _local;
        private readonly List<SimEvent> _events = new List<SimEvent>();
        private bool _sentInitial;
        private bool _ended;

        public int LocalPlayerId => _local.Id;
        public List<RosterEntry> Roster { get; } = new List<RosterEntry>();
        public int MatchSeconds => Sim.Settings.MatchSeconds;
        public bool Autopilot { get; }
        public bool IsOnline => false;
        public Action<Snapshot> OnSnapshot { get; set; }
        public Action<List<SimEvent>> OnEvents { get; set; }
        public Action<List<PlayerResult>> OnEnd { get; set; }

        public LocalMatchDriver(MapData map, MatchSettings settings, string name, Appearance look, bool autopilot)
        {
            Autopilot = autopilot;
            Sim = new MatchSim(map, settings);
            _local = Sim.AddPlayer(name, look, autopilot, autopilot ? BotKind.Explorer : BotKind.None);
            _local.IsBot = false; // still "the human" for results/UI
            Sim.FillBots();
            Sim.Start();
            foreach (var p in Sim.Players)
                Roster.Add(new RosterEntry { Id = p.Id, Name = p.Name, Look = p.Look, IsBot = p.IsBot, Squad = p.Squad });
        }

        public void Poll()
        {
            if (_sentInitial) return;
            _sentInitial = true;
            Deliver();
        }

        private bool _fastForward;

        public void SendInput(InputCmd cmd)
        {
            if (_ended) return;
            if (_fastForward)
            {
                // your squad is out: play the rest of the bot match out quickly (a few hundred ticks per frame) for final results
                for (int i = 0; i < 300 && !Sim.Ended; i++) Sim.Step();
                if (Sim.Ended || Sim.Tick > 200000) { _ended = true; ClientMatch.ResultsFinal = true; ClientMatch.FinalWinner = Sim.WinnerSquad; OnEnd?.Invoke(Sim.Ended ? Sim.Results : Sim.BuildResults()); }
                return;
            }
            if (!_sentInitial) Poll();
            if (!Autopilot) Sim.SubmitInput(_local.Id, cmd);
            Sim.Step();
            Deliver();
        }

        private void Deliver()
        {
            _events.Clear();
            foreach (var e in Sim.Events)
            {
                var ev = e;
                if (SnapshotBuilder.FilterEvent(Sim, _local, ref ev)) _events.Add(ev);
            }
            var snap = new Snapshot();
            SnapshotBuilder.Build(Sim, _local, snap);
            OnSnapshot?.Invoke(snap);
            if (_events.Count > 0) OnEvents?.Invoke(new List<SimEvent>(_events));
            if (!Sim.Ended && !_ended && !Autopilot && Sim.WipeOrder.Contains(_local.Squad)) _fastForward = true;
            if (Sim.Ended && !_ended)
            {
                ClientMatch.ResultsFinal = true;
                _ended = true;
                OnEnd?.Invoke(Sim.Results);
            }
        }

        public void DebugSkip(float seconds) => Sim.DebugSkipTime(seconds);

        public void Dispose() { }
    }
}
