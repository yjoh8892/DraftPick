using DraftPick.Models;

namespace DraftPick.Tests;

/// <summary>
/// 턴 타이머는 서버가 1초마다 <see cref="DraftRoom.Tick"/>을 불러 돌린다.
/// 여기서는 그 호출을 직접 흉내 낸다.
/// </summary>
public class TurnTimerTests
{
    /// <summary>제한시간 1초짜리 턴이 확실히 지나가도록 기다린다.</summary>
    private static void WaitOutTurn() => Thread.Sleep(1200);

    [Fact]
    public void 시간이_다_되면_남은_선수_중_맨_위가_자동_지명된다()
    {
        var room = TestRoom.Started(rounds: 1, teams: 2, players: 2, turnSeconds: 1);
        var onClock = room.CurrentTeam!;
        var top = room.Players[0];

        WaitOutTurn();
        room.Tick();

        Assert.Equal(1, room.PickIndex);
        Assert.Equal(onClock.Id, top.DraftedBy);
    }

    [Fact]
    public void 자동_지명을_끄면_시간이_다_돼도_기다린다()
    {
        var room = TestRoom.Create(rounds: 1, teams: 2, players: 2, turnSeconds: 1);
        room.AutoPickOnTimeout = false;
        room.Start();

        WaitOutTurn();
        room.Tick();

        Assert.Equal(0, room.PickIndex);
        Assert.Null(room.SecondsLeft);
        Assert.True(room.AwaitingHostAfterTimeout);
    }

    [Fact]
    public void 시간이_다_된_뒤에도_수동_지명은_된다()
    {
        var room = TestRoom.Create(rounds: 1, teams: 2, players: 2, turnSeconds: 1);
        room.AutoPickOnTimeout = false;
        room.Start();
        var onClock = room.CurrentTeam!;

        WaitOutTurn();
        room.Tick();

        Assert.Null(room.Pick(room.Players[1].Id, onClock.Id, hostKey: null));
    }

    [Fact]
    public void 무제한이면_마감_시각이_없고_틱으로_진행되지_않는다()
    {
        var room = TestRoom.Started(rounds: 1, teams: 2, players: 2, turnSeconds: 0);

        room.Tick();

        Assert.Null(room.TurnEndsAt);
        Assert.Null(room.SecondsLeft);
        Assert.Equal(0, room.PickIndex);
        Assert.False(room.AwaitingHostAfterTimeout);
    }

    [Fact]
    public void 진행_중이_아니면_틱이_아무_일도_하지_않는다()
    {
        var room = TestRoom.Create(rounds: 1, teams: 2, players: 2, turnSeconds: 1);

        WaitOutTurn();
        room.Tick();

        Assert.Equal(RoomStatus.Setup, room.Status);
        Assert.Equal(0, room.PickIndex);
    }

    [Fact]
    public void 시작_직후에는_진행자를_기다리는_상태가_아니다()
    {
        var room = TestRoom.Started(turnSeconds: 60);

        Assert.False(room.AwaitingHostAfterTimeout);
    }
}
