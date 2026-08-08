using DraftPick.Models;

namespace DraftPick.Tests;

public class ChatTests
{
    [Fact]
    public void 처음에는_아무_말도_없다()
    {
        Assert.Empty(TestRoom.Create().Chat);
    }

    [Fact]
    public void 관전자가_남긴_말은_관전자로_표시된다()
    {
        var room = TestRoom.Create();

        Assert.Null(room.Say("길동", "안녕하세요", teamId: null, hostKey: null));

        var line = Assert.Single(room.Chat);
        Assert.Equal("길동", line.Author);
        Assert.Equal("관전자", line.Label);
        Assert.Equal("안녕하세요", line.Text);
    }

    [Fact]
    public void 팀장이_남긴_말에는_팀_이름과_색이_붙는다()
    {
        var room = TestRoom.Create();
        var team = room.Teams[0];

        room.Say("길동", "잘 부탁드립니다", team.Id, hostKey: null);

        var line = Assert.Single(room.Chat);
        Assert.Equal(team.Name, line.Label);
        Assert.Equal(team.Color, line.Color);
    }

    [Fact]
    public void 진행자가_남긴_말은_진행자로_표시된다()
    {
        var room = TestRoom.Create();

        room.Say("운영자", "곧 시작합니다", teamId: null, hostKey: TestRoom.HostKey);

        Assert.Equal("진행자", Assert.Single(room.Chat).Label);
    }

    [Fact]
    public void 이름표는_화면_말이_아니라_서버가_정한다()
    {
        // 진행자 키 없이 팀만 골라도 진행자로 표시되지 않는다.
        var room = TestRoom.Create();

        room.Say("사칭러", "나 진행자임", room.Teams[0].Id, hostKey: "가짜키");

        Assert.NotEqual("진행자", Assert.Single(room.Chat).Label);
    }

    [Fact]
    public void 없는_팀을_대면_관전자로_떨어진다()
    {
        var room = TestRoom.Create();

        room.Say("길동", "어디팀?", Guid.NewGuid(), hostKey: null);

        Assert.Equal("관전자", Assert.Single(room.Chat).Label);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void 이름이_없으면_남길_수_없다(string? author)
    {
        var room = TestRoom.Create();

        Assert.NotNull(room.Say(author, "내용", null, null));
        Assert.Empty(room.Chat);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void 내용이_비면_남길_수_없다(string? text)
    {
        var room = TestRoom.Create();

        Assert.NotNull(room.Say("길동", text, null, null));
        Assert.Empty(room.Chat);
    }

    [Fact]
    public void 이름과_내용은_앞뒤_공백을_턴다()
    {
        var room = TestRoom.Create();

        room.Say("  길동  ", "  안녕  ", null, null);

        var line = Assert.Single(room.Chat);
        Assert.Equal("길동", line.Author);
        Assert.Equal("안녕", line.Text);
    }

    [Fact]
    public void 너무_긴_이름과_내용은_잘린다()
    {
        var room = TestRoom.Create();

        room.Say(new string('가', 100), new string('나', 1000), null, null);

        var line = Assert.Single(room.Chat);
        Assert.Equal(DraftRoom.MaxNameLength, line.Author.Length);
        Assert.Equal(DraftRoom.MaxChatLength, line.Text.Length);
    }

    [Fact]
    public void 오래된_말은_밀려나고_최근_것만_남는다()
    {
        var room = TestRoom.Create();
        for (var i = 0; i < 250; i++) room.Say("길동", $"{i}번째", null, null);

        Assert.Equal(200, room.Chat.Count);
        Assert.Equal("249번째", room.Chat[^1].Text);
        Assert.DoesNotContain(room.Chat, m => m.Text == "0번째");
    }

    [Fact]
    public void 순번은_계속_올라간다()
    {
        // 화면이 이 값으로 각 줄을 구분한다(@key).
        var room = TestRoom.Create();
        room.Say("길동", "하나", null, null);
        room.Say("길동", "둘", null, null);

        Assert.True(room.Chat[1].Seq > room.Chat[0].Seq);
    }

    [Fact]
    public void 진행_중에도_남길_수_있다()
    {
        var room = TestRoom.Started();

        Assert.Null(room.Say("길동", "화이팅", null, null));
    }
}
