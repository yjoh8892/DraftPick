using DraftPick.Models;

namespace DraftPick.Tests;

/// <summary>
/// 진행자가 시작 전에 "팀장들이 들어왔나"를 확인하는 데 쓰는 집계.
/// 화면(Blazor 회선) 하나가 자리 하나에 앉았다 일어난다.
/// </summary>
public class SeatPresenceTests
{
    [Fact]
    public void 아무도_없으면_0이다()
    {
        var room = TestRoom.Create(teams: 3, players: 6);

        Assert.Equal(0, room.ViewerCount);
        Assert.Equal(0, room.ConnectedTeamCount);
        Assert.All(room.Teams, t => Assert.False(room.IsTeamConnected(t.Id)));
    }

    [Fact]
    public void 팀장이_앉으면_그_팀만_접속으로_잡힌다()
    {
        var room = TestRoom.Create(teams: 3, players: 6);

        room.TakeSeat(room.Teams[0].Id);

        Assert.True(room.IsTeamConnected(room.Teams[0].Id));
        Assert.False(room.IsTeamConnected(room.Teams[1].Id));
        Assert.Equal(1, room.ConnectedTeamCount);
    }

    [Fact]
    public void 진행자와_관전자는_화면_수에만_잡힌다()
    {
        var room = TestRoom.Create(teams: 3, players: 6);
        room.TakeSeat(room.Teams[0].Id);

        room.TakeSeat(null);
        room.TakeSeat(null);

        Assert.Equal(3, room.ViewerCount);
        Assert.Equal(1, room.ConnectedTeamCount);
    }

    [Fact]
    public void 같은_팀으로_두_화면이_들어왔다_하나만_나가면_접속이_유지된다()
    {
        var room = TestRoom.Create(teams: 2, players: 4);
        var team = room.Teams[0].Id;
        room.TakeSeat(team);
        room.TakeSeat(team);

        room.LeaveSeat(team);

        Assert.True(room.IsTeamConnected(team));
        Assert.Equal(1, room.ViewerCount);
    }

    [Fact]
    public void 마지막_화면이_나가면_미접속이_된다()
    {
        var room = TestRoom.Create(teams: 2, players: 4);
        var team = room.Teams[0].Id;
        room.TakeSeat(team);

        room.LeaveSeat(team);

        Assert.False(room.IsTeamConnected(team));
        Assert.Equal(0, room.ConnectedTeamCount);
        Assert.Equal(0, room.ViewerCount);
    }

    [Fact]
    public void 모두_나가면_화면_수가_0으로_돌아온다()
    {
        var room = TestRoom.Create(teams: 2, players: 4);
        room.TakeSeat(room.Teams[0].Id);
        room.TakeSeat(room.Teams[1].Id);
        room.TakeSeat(null);

        room.LeaveSeat(room.Teams[0].Id);
        room.LeaveSeat(room.Teams[1].Id);
        room.LeaveSeat(null);

        Assert.Equal(0, room.ViewerCount);
        Assert.Equal(0, room.ConnectedTeamCount);
    }

    [Fact]
    public void 여러_회선이_동시에_드나들어도_집계가_어긋나지_않는다()
    {
        var room = TestRoom.Create(teams: 4, players: 8);
        var teams = room.Teams.Select(t => t.Id).ToList();

        Parallel.For(0, 200, i =>
        {
            var seat = i % 5 == 0 ? (Guid?)null : teams[i % teams.Count];
            room.TakeSeat(seat);
            room.LeaveSeat(seat);
        });

        Assert.Equal(0, room.ViewerCount);
        Assert.Equal(0, room.ConnectedTeamCount);
    }
}
