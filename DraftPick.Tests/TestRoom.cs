using DraftPick.Models;

namespace DraftPick.Tests;

/// <summary>테스트용 방을 만드는 헬퍼. 팀은 T1, T2… 선수는 P1, P2… 로 채운다.</summary>
internal static class TestRoom
{
    /// <summary>테스트가 진행자로 행세할 때 쓰는 키.</summary>
    public const string HostKey = "테스트-진행자-키";

    public static DraftRoom Create(
        DraftOrderMode mode = DraftOrderMode.Snake,
        int rounds = 2,
        int teams = 2,
        int players = 4,
        int turnSeconds = 60)
    {
        var room = new DraftRoom
        {
            Code = "TEST1",
            HostKey = HostKey,
            OrderMode = mode,
            Rounds = rounds,
            TurnSeconds = turnSeconds,
        };

        for (var i = 0; i < teams; i++) room.AddTeam($"T{i + 1}", $"C{i + 1}");

        var roles = Positions.All.Skip(1).ToArray();
        var tiers = Tiers.All.Skip(1).ToArray();
        for (var i = 0; i < players; i++)
        {
            room.AddPlayer($"P{i + 1}", roles[i % roles.Length], tiers[i % tiers.Length]);
        }

        return room;
    }

    /// <summary>시작까지 마친 방.</summary>
    public static DraftRoom Started(
        DraftOrderMode mode = DraftOrderMode.Snake,
        int rounds = 2,
        int teams = 2,
        int players = 4,
        int turnSeconds = 60)
    {
        var room = Create(mode, rounds, teams, players, turnSeconds);
        room.Start();
        return room;
    }

    /// <summary>진행자 권한으로 남은 선수 중 맨 위를 지명한다.</summary>
    public static void PickAsHost(this DraftRoom room) =>
        room.Pick(room.AvailablePlayers.First().Id, Guid.Empty, HostKey);

    /// <summary>지명 순서를 "T1 T2 T3 …" 형태로 늘어놓는다.</summary>
    public static string PickOrder(this DraftRoom room) =>
        string.Join(" ", Enumerable.Range(0, room.TotalPicks).Select(i => room.TeamAtPick(i)!.Name));
}
