using System;
using System.Collections.Generic;

namespace Veil.Sim
{
    // Gateway (WebSocket) protocol shared by Veil.Server and the Unity client.
    // Every frame is a GwEnvelope; its payload `d` is the JSON of one of the DTOs below (double-encoded so
    // Unity's JsonUtility can parse it without dynamic JSON). Fields are public and match JSON names exactly.

    public static class Gw
    {
        public const int Version = 1;

        // client → server requests
        public const string Hello = "hello", Ping = "ping";
        public const string FriendList = "friend.list", FriendRequest = "friend.request", FriendAccept = "friend.accept",
            FriendDecline = "friend.decline", FriendCancel = "friend.cancel", FriendRemove = "friend.remove";
        public const string PartyCreate = "party.create", PartyJoin = "party.join", PartyLeave = "party.leave",
            PartyInvite = "party.invite", InviteAccept = "party.invite.accept", InviteDecline = "party.invite.decline",
            PartyReady = "party.ready", PartyKick = "party.kick", PartyPromote = "party.promote", PartyStart = "party.start",
            PartyCancel = "party.cancel", PartyLook = "party.look", MatchRejoin = "match.rejoin";

        // server → client pushes
        public const string Welcome = "welcome", Pong = "pong", Reply = "reply";
        public const string FriendsState = "friends.state", Presence = "friend.presence", PartyState = "party.state",
            Invite = "party.invite.incoming", InviteGone = "party.invite.gone", MatchAssigned = "match.assigned",
            MatchFinished = "match.finished", Notice = "notice", Kicked = "kicked";
    }

    public enum PresenceStatus { Offline = 0, Online = 1, InParty = 2, InQueue = 3, InMatch = 4 }

    public enum PartyPhase { Idle = 0, Queued = 1, InMatch = 2 }

    [Serializable] public sealed class GwEnvelope { public string t = ""; public string id = ""; public bool ok = true; public string err = ""; public string d = ""; }

    [Serializable] public sealed class GwHello { public string id = ""; public string token = ""; public string name = ""; public string look = ""; public int version = Gw.Version; }
    [Serializable] public sealed class GwWelcome { public string id = ""; public string name = ""; public string handle = ""; public long serverTime; public int voicePort; }
    [Serializable] public sealed class GwPing { public long clientMs; public int rttMs; }

    [Serializable] public sealed class GwText { public string text = ""; }
    [Serializable] public sealed class GwId { public string id = ""; }
    [Serializable] public sealed class GwRequestId { public long requestId; }
    [Serializable] public sealed class GwFlag { public bool value; }
    [Serializable] public sealed class GwStart { public int seconds = 900; }

    [Serializable]
    public sealed class FriendEntry
    {
        public string id = "", name = "", handle = "", look = "";
        public int level;
        public int status;            // PresenceStatus
        public string detail = "";    // "In match · 6 min left"
        public long requestId;        // for incoming / outgoing requests
        public bool joinable;         // in a party with room
        public string partyCode = "";
    }

    [Serializable]
    public sealed class FriendsState
    {
        public List<FriendEntry> friends = new List<FriendEntry>();
        public List<FriendEntry> incoming = new List<FriendEntry>();
        public List<FriendEntry> outgoing = new List<FriendEntry>();
    }

    [Serializable]
    public sealed class PartyMemberInfo
    {
        public string id = "", name = "", handle = "", look = "";
        public int level;
        public bool ready, leader, online;
        public int ping;
    }

    [Serializable] public sealed class PendingInvite { public string to = "", name = ""; }

    [Serializable]
    public sealed class PartyInfo
    {
        public string id = "", code = "", leader = "";
        public int phase;              // PartyPhase
        public int seconds = 900;
        public int queueSeconds;
        public int matchId = -1;
        public List<PartyMemberInfo> members = new List<PartyMemberInfo>();
        public List<PendingInvite> pending = new List<PendingInvite>();
        public string voiceChannel = "", voiceToken = "";
        public bool Empty => string.IsNullOrEmpty(id);
    }

    [Serializable]
    public sealed class IncomingInvite
    {
        public string inviteId = "", fromId = "", fromName = "", fromHandle = "", partyCode = "";
        public int members;
        public int expiresIn;
    }

    [Serializable]
    public sealed class MatchAssignedMsg
    {
        public int matchId;
        public string host = "";       // empty = same host as the Gateway
        public int port;
        public string ticket = "";
        public int squad;
        public int seconds;
        public bool rejoin;
        public string voiceChannel = "", voiceToken = "";
    }
}
