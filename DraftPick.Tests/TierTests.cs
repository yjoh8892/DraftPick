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

    [Fact]
    public void 자동_지명은_정렬된_목록의_맨_위를_고른다()
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

        Assert.Equal("센선수", room.Players[0].Name);
        Assert.True(room.Players[0].IsDrafted);
    }
}
