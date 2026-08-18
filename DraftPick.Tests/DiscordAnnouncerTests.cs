using DraftPick.Models;
using DraftPick.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace DraftPick.Tests;

public class DiscordAnnouncerTests
{
    [Theory]
    [InlineData("https://discord.com/api/webhooks/123/abc")]
    [InlineData("https://discordapp.com/api/webhooks/123/abc")]
    [InlineData("https://ptb.discord.com/api/webhooks/123/abc")]
    [InlineData("  https://discord.com/api/webhooks/123/abc  ")]
    public void 디스코드_웹훅_주소를_받아준다(string url)
    {
        Assert.True(DiscordAnnouncer.IsWebhookUrl(url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("그냥 글자")]
    [InlineData("http://discord.com/api/webhooks/123/abc")]      // https가 아님
    [InlineData("https://discord.com/api/guilds/123")]            // 웹훅 경로가 아님
    [InlineData("https://evil.example.com/api/webhooks/123/abc")] // 다른 호스트
    [InlineData("http://localhost:5199/admin")]                   // 내부망 찔러보기
    [InlineData("https://discord.com.evil.example/api/webhooks/1")]
    public void 그_외의_주소는_거절한다(string? url)
    {
        Assert.False(DiscordAnnouncer.IsWebhookUrl(url));
    }

    [Fact]
    public void 짧은_글은_한_덩어리로_나간다()
    {
        var chunks = DiscordAnnouncer.Split("한 줄\n두 줄").ToList();

        Assert.Equal("한 줄\n두 줄", Assert.Single(chunks));
    }

    [Fact]
    public void 긴_글은_줄_단위로_나뉜다()
    {
        var line = new string('가', 500);
        var content = string.Join("\n", Enumerable.Repeat(line, 10));   // 약 5,000자

        var chunks = DiscordAnnouncer.Split(content).ToList();

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.True(c.Length <= 1900, $"{c.Length}자"));
        // 줄이 잘리거나 사라지지 않았는지
        Assert.Equal(10, chunks.Sum(c => c.Split('\n').Length));
    }

    [Fact]
    public void 한_줄이_한도를_넘으면_그_줄만_잘린다()
    {
        var chunks = DiscordAnnouncer.Split(new string('가', 5000)).ToList();

        Assert.Equal(1900, Assert.Single(chunks).Length);
    }

    [Fact]
    public async Task 웹훅_주소가_아니면_보내지_않고_사유를_준다()
    {
        var announcer = new DiscordAnnouncer(new UnusableHttpClientFactory(), NullLogger<DiscordAnnouncer>.Instance);

        var problem = await announcer.PostAsync("https://evil.example.com/hook", "내용");

        Assert.NotNull(problem);
    }

    [Fact]
    public async Task 보낼_내용이_없으면_사유를_준다()
    {
        var announcer = new DiscordAnnouncer(new UnusableHttpClientFactory(), NullLogger<DiscordAnnouncer>.Instance);

        var problem = await announcer.PostAsync("https://discord.com/api/webhooks/1/a", "   ");

        Assert.NotNull(problem);
    }

    /// <summary>실제로 요청이 나가면 터진다. "보내기 전에 걸러야 한다"를 확인하는 용도.</summary>
    private sealed class UnusableHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            throw new InvalidOperationException("이 경우에는 요청을 보내면 안 됩니다.");
    }
}

public class ResultTextTests
{
    [Fact]
    public void 결과_글에_팀과_지명_순서가_들어간다()
    {
        var room = TestRoom.Started(rounds: 2, teams: 2, players: 4);
        room.Title = "8월 내전";
        for (var i = 0; i < 4; i++) room.PickAsHost();

        var text = room.BuildResultText();

        Assert.Contains("8월 내전", text);
        Assert.Contains("스네이크", text);
        Assert.Contains("2라운드", text);
        Assert.All(room.Teams, t => Assert.Contains(t.Name, text));
        Assert.All(room.Players, p => Assert.Contains(p.Name, text));
    }

    [Fact]
    public void 팀장은_지명_명단에_없으므로_따로_한_줄로_들어간다()
    {
        var room = TestRoom.Started(rounds: 1, teams: 2, players: 2);
        room.PickAsHost();
        room.PickAsHost();

        var text = room.BuildResultText();

        Assert.Contains($"■ {room.Teams[0].Name}", text);
        Assert.Contains($"팀장. {room.Teams[0].Captain}", text);
    }

    [Fact]
    public void 팀장_이름이_비면_그_줄은_없다()
    {
        var room = new DraftRoom { Code = "X", HostKey = TestRoom.HostKey, Rounds = 1 };
        room.AddTeam("이름없는팀장팀");
        room.AddTeam("T2");
        room.AddPlayer("갑");
        room.AddPlayer("을");
        room.Start();
        room.PickAsHost();
        room.PickAsHost();

        Assert.DoesNotContain("팀장.", room.BuildResultText());
    }

    [Fact]
    public void 미지정_포지션은_대괄호를_붙이지_않는다()
    {
        var room = new DraftRoom { Code = "X", HostKey = TestRoom.HostKey, Rounds = 1 };
        room.AddTeam("T1");
        room.AddTeam("T2");
        room.AddPlayer("무직자");
        room.AddPlayer("타격수", "타격대");
        room.Start();
        room.PickAsHost();
        room.PickAsHost();

        var text = room.BuildResultText();

        Assert.Contains("무직자", text);
        Assert.DoesNotContain($"무직자 [{Positions.Unset}]", text);
        Assert.Contains("타격수 [타격대]", text);
    }
}

public class ResultAnnouncementTests
{
    private static DraftRoom Finished(string webhook = "https://discord.com/api/webhooks/1/a")
    {
        var room = TestRoom.Started(rounds: 1, teams: 2, players: 2);
        room.WebhookUrl = webhook;
        room.PickAsHost();
        room.PickAsHost();
        return room;
    }

    [Fact]
    public void 끝나면_한_번만_올릴_차례가_온다()
    {
        var room = Finished();

        Assert.Equal(RoomStatus.Finished, room.Status);
        Assert.True(room.TryClaimResultAnnouncement());
        Assert.False(room.TryClaimResultAnnouncement());
    }

    [Fact]
    public void 웹훅이_없으면_올리지_않는다()
    {
        Assert.False(Finished(webhook: "").TryClaimResultAnnouncement());
    }

    [Fact]
    public void 끝나기_전에는_올리지_않는다()
    {
        var room = TestRoom.Started(rounds: 2, teams: 2, players: 4);
        room.WebhookUrl = "https://discord.com/api/webhooks/1/a";

        Assert.False(room.TryClaimResultAnnouncement());
    }

    [Fact]
    public void 되돌려서_다시_끝나면_고쳐진_결과를_올린다()
    {
        var room = Finished();
        room.TryClaimResultAnnouncement();

        room.UndoLastPick();
        room.PickAsHost();

        Assert.Equal(RoomStatus.Finished, room.Status);
        Assert.True(room.TryClaimResultAnnouncement());
    }

    [Fact]
    public void 설정으로_되돌린_뒤_다시_치르면_또_올린다()
    {
        var room = Finished();
        room.TryClaimResultAnnouncement();

        room.ResetToSetup();
        room.Start();
        room.PickAsHost();
        room.PickAsHost();

        Assert.True(room.TryClaimResultAnnouncement());
    }
}
