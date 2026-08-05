using DraftPick.Models;

namespace DraftPick.Tests;

/// <summary>
/// 라운드 수·제한시간·지명 방식은 진행 중인 드래프트의 계산에 그대로 쓰인다.
/// 화면의 min/max 속성이 아니라 방이 직접 막는지 확인한다.
/// </summary>
public class RoomSettingsTests
{
    [Fact]
    public void 라운드는_허용_범위로_잘린다()
    {
        var room = TestRoom.Create();

        room.Rounds = 999;
        Assert.Equal(DraftRoom.MaxRounds, room.Rounds);

        room.Rounds = 0;
        Assert.Equal(DraftRoom.MinRounds, room.Rounds);
    }

    [Fact]
    public void 제한시간은_음수가_되지_않고_상한을_넘지_않는다()
    {
        var room = TestRoom.Create();

        room.TurnSeconds = -5;
        Assert.Equal(0, room.TurnSeconds);

        room.TurnSeconds = 99_999;
        Assert.Equal(DraftRoom.MaxTurnSeconds, room.TurnSeconds);
    }

    [Fact]
    public void 시작한_뒤에는_설정을_바꿀_수_없다()
    {
        var room = TestRoom.Create(DraftOrderMode.Snake, rounds: 2, teams: 2, players: 4, turnSeconds: 30);
        room.Start();

        room.Rounds = 7;
        room.TurnSeconds = 5;
        room.OrderMode = DraftOrderMode.Sequential;

        Assert.Equal(2, room.Rounds);
        Assert.Equal(30, room.TurnSeconds);
        Assert.Equal(DraftOrderMode.Snake, room.OrderMode);
    }

    [Fact]
    public void 시작한_뒤에는_팀과_선수를_손댈_수_없다()
    {
        var room = TestRoom.Started(rounds: 1, teams: 2, players: 2);
        var teamCount = room.Teams.Count;
        var playerCount = room.Players.Count;

        Assert.Null(room.AddTeam("늦둥이"));
        room.RemoveTeam(room.Teams[0].Id);
        room.RemovePlayer(room.Players[0].Id);
        room.ClearPlayers();

        Assert.Equal(teamCount, room.Teams.Count);
        Assert.Equal(playerCount, room.Players.Count);
    }

    [Fact]
    public void 팀_순서를_위아래로_옮길_수_있다()
    {
        var room = TestRoom.Create(teams: 3, rounds: 1, players: 3);
        var second = room.Teams[1].Id;

        room.MoveTeam(second, -1);

        Assert.Equal(second, room.Teams[0].Id);
    }

    [Fact]
    public void 맨_끝에서_더_옮기려_하면_아무_일도_없다()
    {
        var room = TestRoom.Create(teams: 3, rounds: 1, players: 3);
        var order = room.Teams.Select(t => t.Id).ToList();

        room.MoveTeam(order[0], -1);
        room.MoveTeam(order[2], 1);

        Assert.Equal(order, room.Teams.Select(t => t.Id));
    }

    [Fact]
    public void 팀_색은_팔레트를_돌아가며_배정된다()
    {
        var room = new DraftRoom { Code = "X", HostKey = TestRoom.HostKey };
        for (var i = 0; i < TeamColors.Palette.Length + 1; i++) room.AddTeam($"T{i}");

        Assert.Equal(TeamColors.Palette[0], room.Teams[0].Color);
        Assert.Equal(TeamColors.Palette[0], room.Teams[^1].Color);
    }

    [Fact]
    public void 이름_없이_팀을_추가하면_번호가_붙는다()
    {
        var room = new DraftRoom { Code = "X", HostKey = TestRoom.HostKey };

        room.AddTeam();
        room.AddTeam();

        Assert.Equal(["팀 1", "팀 2"], room.Teams.Select(t => t.Name));
    }

    [Theory]
    [InlineData(DraftOrderMode.Snake, "스네이크")]
    [InlineData(DraftOrderMode.Sequential, "순차")]
    public void 지명_방식_라벨은_한_곳에서_나온다(DraftOrderMode mode, string expected)
    {
        Assert.Equal(expected, mode.Label());
        Assert.NotEqual(DraftOrderMode.Snake.Pattern(), DraftOrderMode.Sequential.Pattern());
    }
}
