using DraftPick.Models;

namespace DraftPick.Tests;

public class TierTests
{
    [Theory]
    [InlineData("불멸", "불멸")]          // 정확한 이름은 그대로
    [InlineData("다이아", "다이아몬드")]   // 줄여 쓴 것도 받아준다
    [InlineData("플래", "플래티넘")]
    [InlineData("골드해적단", "")]        // 어디에도 안 맞으면 미지정
    [InlineData("  ", "")]
    [InlineData(null, "")]
    public void 티어를_목록에_맞춰_정규화한다(string? raw, string expected)
    {
        Assert.Equal(expected, Tiers.Normalize(raw));
    }

    [Fact]
    public void 랭크는_아이언에서_레디언트로_올라간다()
    {
        Assert.True(Tiers.RankOf("레디언트") > Tiers.RankOf("불멸"));
        Assert.True(Tiers.RankOf("불멸") > Tiers.RankOf("골드"));
        Assert.True(Tiers.RankOf("골드") > Tiers.RankOf("아이언"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("없는티어")]
    [InlineData(null)]
    public void 알_수_없는_티어는_랭크가_0이다(string? tier)
    {
        Assert.Equal(0, Tiers.RankOf(tier));
    }

    [Fact]
    public void 정렬하면_높은_티어가_앞에_오고_미지정은_맨_뒤로_간다()
    {
        var room = new DraftRoom { Code = "X", HostKey = TestRoom.HostKey };
        room.AddPlayer("A", tier: "아이언");
        room.AddPlayer("B", tier: Tiers.Unset);
        room.AddPlayer("C", tier: "레디언트");
        room.AddPlayer("D", tier: "골드");

        room.SortPlayersByTier();

        Assert.Equal(["C", "D", "A", "B"], room.Players.Select(p => p.Name));
    }

    /// <summary>
    /// 자동 지명은 진행자가 티어순 정렬을 눌렀는지에 좌우되면 안 된다.
    /// 시간 초과는 정신없을 때 일어나므로, 그때 엉뚱한 선수가 들어가면 분쟁이 된다.
    /// </summary>
    private static DraftRoom TimeoutRoom(params (string Name, string Tier)[] players)
    {
        var room = new DraftRoom { Code = "X", HostKey = TestRoom.HostKey, Rounds = 1, TurnSeconds = 1 };
        room.AddTeam("T1");
        room.AddTeam("T2");
        foreach (var (name, tier) in players) room.AddPlayer(name, tier: tier);
        room.Start();

        Thread.Sleep(1200);
        room.Tick();
        return room;
    }

    [Fact]
    public void 정렬하지_않아도_최고_티어가_자동_지명된다()
    {
        // 명단 맨 위는 약한 선수지만 뽑히면 안 된다.
        var room = TimeoutRoom(("약한선수", "아이언"), ("센선수", "레디언트"));

        Assert.Equal("센선수", Assert.Single(room.Players.Where(p => p.IsDrafted)).Name);
    }

    [Fact]
    public void 같은_티어끼리는_명단_위쪽이_먼저다()
    {
        var room = TimeoutRoom(("먼저적은사람", "골드"), ("나중에적은사람", "골드"));

        Assert.Equal("먼저적은사람", Assert.Single(room.Players.Where(p => p.IsDrafted)).Name);
    }

    [Fact]
    public void 티어_미지정은_맨_뒤로_밀린다()
    {
        var room = TimeoutRoom(("모름", Tiers.Unset), ("아이언선수", "아이언"));

        Assert.Equal("아이언선수", Assert.Single(room.Players.Where(p => p.IsDrafted)).Name);
    }

    [Fact]
    public void 모두_티어가_없으면_명단_순서를_따른다()
    {
        var room = TimeoutRoom(("첫째", Tiers.Unset), ("둘째", Tiers.Unset));

        Assert.Equal("첫째", Assert.Single(room.Players.Where(p => p.IsDrafted)).Name);
    }

    [Fact]
    public void 티어순_정렬을_눌러도_결과는_같다()
    {
        var room = new DraftRoom { Code = "X", HostKey = TestRoom.HostKey, Rounds = 1, TurnSeconds = 1 };
        room.AddTeam("T1");
        room.AddTeam("T2");
        room.AddPlayer("약한선수", tier: "아이언");
        room.AddPlayer("센선수", tier: "레디언트");
        room.SortPlayersByTier();
        room.Start();

        Thread.Sleep(1200);
        room.Tick();

        Assert.Equal("센선수", Assert.Single(room.Players.Where(p => p.IsDrafted)).Name);
    }
}
