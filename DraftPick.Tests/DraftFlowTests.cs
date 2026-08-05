using DraftPick.Models;

namespace DraftPick.Tests;

public class DraftFlowTests
{
    // ── 시작 검증 ────────────────────────────────────────────────────────

    [Fact]
    public void 팀이_하나면_시작할_수_없다()
    {
        var room = TestRoom.Create(teams: 1, rounds: 1, players: 5);

        Assert.NotNull(room.Start());
        Assert.Equal(RoomStatus.Setup, room.Status);
    }

    [Fact]
    public void 선수가_모자라면_몇_명_필요한지_알려준다()
    {
        var room = TestRoom.Create(teams: 3, rounds: 3, players: 5);

        var problem = room.ValidateForStart();

        Assert.NotNull(problem);
        Assert.Contains("9명", problem);
        Assert.Contains("5명", problem);
    }

    [Fact]
    public void 이름이_빈_팀이_있으면_시작할_수_없다()
    {
        var room = TestRoom.Create(teams: 2, rounds: 1, players: 2);
        room.Teams[0].Name = "   ";

        Assert.NotNull(room.ValidateForStart());
    }

    [Fact]
    public void 시작하면_진행_중이_되고_턴_시계가_돈다()
    {
        var room = TestRoom.Create(rounds: 2, teams: 2, players: 4);

        Assert.Null(room.Start());
        Assert.Equal(RoomStatus.Running, room.Status);
        Assert.NotNull(room.TurnEndsAt);
        Assert.Equal(1, room.CurrentRound);
    }

    [Fact]
    public void 이미_시작한_방은_다시_시작할_수_없다()
    {
        var room = TestRoom.Started();

        Assert.NotNull(room.Start());
    }

    // ── 지명 권한 ────────────────────────────────────────────────────────

    [Fact]
    public void 자기_차례가_아니면_지명할_수_없다()
    {
        var room = TestRoom.Started();
        var notOnClock = room.Teams[1];

        var error = room.Pick(room.Players[0].Id, notOnClock.Id, hostKey: null);

        Assert.NotNull(error);
        Assert.False(room.Players[0].IsDrafted);
    }

    [Fact]
    public void 자기_차례면_지명할_수_있다()
    {
        var room = TestRoom.Started();
        var onClock = room.Teams[0];
        var player = room.Players[0];

        Assert.Null(room.Pick(player.Id, onClock.Id, hostKey: null));
        Assert.Equal(onClock.Id, player.DraftedBy);
        Assert.Equal(1, player.PickNumber);
    }

    [Fact]
    public void 이미_지명된_선수는_다시_지명할_수_없다()
    {
        var room = TestRoom.Started();
        var player = room.Players[0];
        room.Pick(player.Id, room.Teams[0].Id, hostKey: null);

        Assert.NotNull(room.Pick(player.Id, room.Teams[1].Id, hostKey: null));
    }

    [Fact]
    public void 없는_선수는_지명할_수_없다()
    {
        var room = TestRoom.Started();

        Assert.NotNull(room.Pick(Guid.NewGuid(), room.Teams[0].Id, hostKey: null));
    }

    [Fact]
    public void 진행자는_아무_차례에나_대리_지명할_수_있다()
    {
        var room = TestRoom.Started();

        Assert.Null(room.Pick(room.Players[0].Id, Guid.Empty, TestRoom.HostKey));
    }

    [Fact]
    public void 가짜_진행자_키로는_대리_지명할_수_없다()
    {
        var room = TestRoom.Started();

        var error = room.Pick(room.Players[0].Id, room.Teams[1].Id, hostKey: "가짜");

        Assert.NotNull(error);
        Assert.False(room.Players[0].IsDrafted);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("틀린키")]
    public void 진행자_판정은_키가_정확히_맞을_때만_참이다(string? key)
    {
        var room = TestRoom.Create();

        Assert.False(room.IsHost(key));
        Assert.True(room.IsHost(TestRoom.HostKey));
    }

    // ── 진행과 완료 ──────────────────────────────────────────────────────

    [Fact]
    public void 모든_픽이_끝나면_완료되고_타이머가_멈춘다()
    {
        var room = TestRoom.Started(rounds: 2, teams: 2, players: 4);

        for (var i = 0; i < 4; i++) room.PickAsHost();

        Assert.Equal(RoomStatus.Finished, room.Status);
        Assert.Null(room.TurnEndsAt);
        Assert.All(room.Teams, t => Assert.Equal(2, room.RosterOf(t.Id).Count()));
    }

    [Fact]
    public void 턴_넘기기는_그_팀을_건너뛴다()
    {
        var room = TestRoom.Started(rounds: 2, teams: 2, players: 4);
        var skipped = room.CurrentTeam!;

        room.SkipTurn();

        Assert.NotEqual(skipped.Id, room.CurrentTeam!.Id);
        Assert.Empty(room.RosterOf(skipped.Id));
    }

    [Fact]
    public void 로스터는_지명_순서대로_정렬된다()
    {
        var room = TestRoom.Started(rounds: 3, teams: 2, players: 6);

        for (var i = 0; i < 6; i++) room.PickAsHost();

        foreach (var team in room.Teams)
        {
            var picks = room.RosterOf(team.Id).Select(p => p.PickNumber!.Value).ToList();
            Assert.Equal(picks.OrderBy(n => n), picks);
        }
    }

    [Fact]
    public void 팀별_로스터를_한번에_구한_결과는_개별_조회와_같다()
    {
        var room = TestRoom.Started(rounds: 2, teams: 3, players: 6);
        for (var i = 0; i < 6; i++) room.PickAsHost();

        var grouped = room.RostersByTeam();

        Assert.Equal(3, grouped.Count);
        Assert.Equal(6, grouped.Values.Sum(r => r.Count));
        Assert.All(room.Teams, t =>
            Assert.Equal(room.RosterOf(t.Id).Select(p => p.Id), grouped[t.Id].Select(p => p.Id)));
    }

    // ── 되돌리기 ─────────────────────────────────────────────────────────

    [Fact]
    public void 되돌리면_선수가_풀로_돌아온다()
    {
        var room = TestRoom.Started();
        var player = room.Players[0];
        room.Pick(player.Id, room.Teams[0].Id, hostKey: null);

        Assert.Null(room.UndoLastPick());
        Assert.False(player.IsDrafted);
        Assert.Null(player.PickNumber);
        Assert.Equal(0, room.PickIndex);
    }

    [Fact]
    public void 지명_전에는_되돌릴_것이_없다()
    {
        var room = TestRoom.Started();

        Assert.False(room.CanUndo);
        Assert.NotNull(room.UndoLastPick());
    }

    [Fact]
    public void 완료된_뒤_되돌리면_다시_진행_중이_된다()
    {
        var room = TestRoom.Started(rounds: 2, teams: 2, players: 4);
        for (var i = 0; i < 4; i++) room.PickAsHost();

        room.UndoLastPick();

        Assert.Equal(RoomStatus.Running, room.Status);
        Assert.True(room.CanUndo);
    }

    // ── 일시정지 ─────────────────────────────────────────────────────────

    [Fact]
    public void 일시정지하면_남은_시간이_보존되고_지명이_막힌다()
    {
        var room = TestRoom.Started(turnSeconds: 30);

        room.Pause();

        Assert.Equal(RoomStatus.Paused, room.Status);
        Assert.InRange(room.SecondsLeft!.Value, 25, 30);
        Assert.NotNull(room.Pick(room.Players[0].Id, room.Teams[0].Id, TestRoom.HostKey));
    }

    [Fact]
    public void 재개하면_타이머가_다시_흐른다()
    {
        var room = TestRoom.Started(turnSeconds: 30);
        room.Pause();

        room.Resume();

        Assert.Equal(RoomStatus.Running, room.Status);
        Assert.NotNull(room.TurnEndsAt);
    }

    [Fact]
    public void 진행_중이_아니면_일시정지해도_변화가_없다()
    {
        var room = TestRoom.Create();

        room.Pause();

        Assert.Equal(RoomStatus.Setup, room.Status);
    }

    // ── 설정으로 되돌리기 ────────────────────────────────────────────────

    [Fact]
    public void 설정으로_되돌리면_지명이_모두_풀린다()
    {
        var room = TestRoom.Started(rounds: 1, teams: 2, players: 2);
        room.PickAsHost();

        room.ResetToSetup();

        Assert.Equal(RoomStatus.Setup, room.Status);
        Assert.Equal(0, room.PickIndex);
        Assert.All(room.Players, p => Assert.False(p.IsDrafted));
        Assert.Null(room.Start());
    }

    [Fact]
    public void 팀을_지우면_그_팀이_뽑았던_선수가_풀로_돌아온다()
    {
        var room = TestRoom.Started(rounds: 1, teams: 2, players: 2);
        var team = room.CurrentTeam!;
        room.PickAsHost();
        room.ResetToSetup();
        room.Start();
        room.PickAsHost();
        var drafted = room.RosterOf(team.Id).ToList();
        room.ResetToSetup();

        room.RemoveTeam(team.Id);

        Assert.DoesNotContain(room.Teams, t => t.Id == team.Id);
        Assert.All(drafted, p => Assert.False(p.IsDrafted));
    }
}
