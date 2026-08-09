using DraftPick.Models;

namespace DraftPick.Tests;

/// <summary>
/// 화면이 조용히 바뀌면 무슨 일이 있었는지 놓치기 쉬워서, 방이 마지막 사건을 들고 있다가 알린다.
/// </summary>
public class DraftEventTests
{
    [Fact]
    public void 시작_직후에는_알릴_사건이_없다()
    {
        var room = TestRoom.Started();

        Assert.Null(room.LastEvent);
    }

    [Fact]
    public void 지명하면_어느_팀이_누구를_뽑았는지_남는다()
    {
        var room = TestRoom.Started();
        var team = room.CurrentTeam!;
        var player = room.Players[0];

        room.Pick(player.Id, team.Id, hostKey: null);

        var e = room.LastEvent!;
        Assert.Equal(DraftEventKind.Pick, e.Kind);
        Assert.Equal(1, e.PickNumber);
        Assert.Equal(team.Name, e.TeamName);
        Assert.Equal(team.Color, e.TeamColor);
        Assert.Equal(player.Name, e.PlayerName);
    }

    [Fact]
    public void 시간_초과로_들어간_지명은_따로_구분된다()
    {
        var room = TestRoom.Started(rounds: 1, teams: 2, players: 2, turnSeconds: 1);

        Thread.Sleep(1200);
        room.Tick();

        // 자동 지명은 목록 맨 위가 아니라 티어가 가장 높은 사람을 고른다(P1 아이언 < P2 브론즈).
        Assert.Equal(DraftEventKind.AutoPick, room.LastEvent!.Kind);
        Assert.Equal("P2", room.LastEvent.PlayerName);
    }

    [Fact]
    public void 턴을_넘기면_넘긴_팀이_남는다()
    {
        var room = TestRoom.Started(rounds: 2, teams: 2, players: 4);
        var skipped = room.CurrentTeam!;

        room.SkipTurn();

        var e = room.LastEvent!;
        Assert.Equal(DraftEventKind.Skip, e.Kind);
        Assert.Equal(skipped.Name, e.TeamName);
        Assert.Equal("", e.PlayerName);
    }

    [Fact]
    public void 되돌리면_무엇이_취소됐는지_남는다()
    {
        var room = TestRoom.Started();
        var team = room.CurrentTeam!;
        var player = room.Players[0];
        room.Pick(player.Id, team.Id, hostKey: null);

        room.UndoLastPick();

        var e = room.LastEvent!;
        Assert.Equal(DraftEventKind.Undo, e.Kind);
        Assert.Equal(player.Name, e.PlayerName);
        Assert.Equal(team.Name, e.TeamName);
        Assert.Equal(team.Color, e.TeamColor);
    }

    [Fact]
    public void 설정으로_되돌리면_사건도_지워진다()
    {
        var room = TestRoom.Started(rounds: 1, teams: 2, players: 2);
        room.PickAsHost();

        room.ResetToSetup();

        Assert.Null(room.LastEvent);
    }

    [Fact]
    public void 사건이_바뀌면_다른_값이_된다()
    {
        // 화면은 이 값이 달라지는 것으로 새 알림인지 판단한다(@key).
        var room = TestRoom.Started(rounds: 2, teams: 2, players: 4);
        room.PickAsHost();
        var first = room.LastEvent;

        room.PickAsHost();

        Assert.NotEqual(first, room.LastEvent);
    }

    [Fact]
    public void 같은_내용의_사건은_같은_값으로_취급된다()
    {
        var a = new DraftEvent(DraftEventKind.Pick, 1, "T1", "#fff", "홍길동");
        var b = new DraftEvent(DraftEventKind.Pick, 1, "T1", "#fff", "홍길동");

        Assert.Equal(a, b);
    }
}
