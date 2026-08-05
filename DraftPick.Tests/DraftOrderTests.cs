using DraftPick.Models;

namespace DraftPick.Tests;

public class DraftOrderTests
{
    [Fact]
    public void 스네이크는_라운드마다_순서가_뒤집힌다()
    {
        var room = TestRoom.Create(DraftOrderMode.Snake, rounds: 3, teams: 3, players: 9);

        Assert.Equal("T1 T2 T3 T3 T2 T1 T1 T2 T3", room.PickOrder());
    }

    [Fact]
    public void 순차는_매_라운드_같은_순서다()
    {
        var room = TestRoom.Create(DraftOrderMode.Sequential, rounds: 3, teams: 3, players: 9);

        Assert.Equal("T1 T2 T3 T1 T2 T3 T1 T2 T3", room.PickOrder());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void 범위를_벗어난_픽에는_팀이_없다(int index)
    {
        var room = TestRoom.Create(teams: 3, rounds: 2, players: 6);

        Assert.Null(room.TeamAtPick(index));
    }

    [Fact]
    public void 전체_픽_수는_팀_곱하기_라운드다()
    {
        var room = TestRoom.Create(teams: 4, rounds: 5, players: 20);

        Assert.Equal(20, room.TotalPicks);
    }

    [Fact]
    public void 다음_순서_미리보기는_남은_픽까지만_준다()
    {
        var room = TestRoom.Started(rounds: 1, teams: 2, players: 2);

        var upcoming = room.UpcomingPicks(10).ToList();

        Assert.Equal(2, upcoming.Count);
        Assert.Equal(1, upcoming[0].PickNumber);
        Assert.Equal("T1", upcoming[0].Team.Name);
    }

    [Fact]
    public void 순서_섞기는_팀을_잃지_않는다()
    {
        var room = TestRoom.Create(teams: 6, rounds: 1, players: 6);
        var before = room.Teams.Select(t => t.Id).ToList();

        room.ShuffleTeams();

        Assert.Equal(6, room.Teams.Count);
        Assert.All(before, id => Assert.Contains(id, room.Teams.Select(t => t.Id)));
        Assert.Equal(6, room.Teams.Select(t => t.Color).Distinct().Count());
    }

    [Fact]
    public void 순서_섞기는_실제로_순서를_바꾼다()
    {
        var room = TestRoom.Create(teams: 6, rounds: 1, players: 6);
        var before = room.Teams.Select(t => t.Id).ToList();

        // 6! 가지라 20번 섞어 한 번도 안 바뀔 확률은 무시할 수준이다.
        var changed = Enumerable.Range(0, 20).Any(_ =>
        {
            room.ShuffleTeams();
            return !room.Teams.Select(t => t.Id).SequenceEqual(before);
        });

        Assert.True(changed);
    }

    [Fact]
    public void 시작한_뒤에는_순서를_섞을_수_없다()
    {
        var room = TestRoom.Started(teams: 4, rounds: 1, players: 4);
        var order = room.Teams.Select(t => t.Id).ToList();

        room.ShuffleTeams();

        Assert.Equal(order, room.Teams.Select(t => t.Id));
    }
}
