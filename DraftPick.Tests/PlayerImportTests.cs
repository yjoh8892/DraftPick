using DraftPick.Models;

namespace DraftPick.Tests;

public class PlayerImportTests
{
    private static DraftRoom Empty() => new() { Code = "X", HostKey = TestRoom.HostKey };

    private static IReadOnlyList<Player> Paste(string text)
    {
        var room = Empty();
        room.ImportPlayers(text);
        return room.Players;
    }

    // ── 직접 타이핑하는 경우 ─────────────────────────────────────────────

    [Fact]
    public void 쉼표로_이름_포지션_티어를_받는다()
    {
        var players = Paste("홍길동,타격대,다이아몬드");

        var p = Assert.Single(players);
        Assert.Equal("홍길동", p.Name);
        Assert.Equal("타격대", p.Position);
        Assert.Equal("다이아몬드", p.Tier);
    }

    [Fact]
    public void 뒤_두_칸은_생략할_수_있다()
    {
        var players = Paste("김철수,감시자\n이영희");

        Assert.Equal(2, players.Count);
        Assert.Equal(Tiers.Unset, players[0].Tier);
        Assert.Equal(Positions.Unset, players[1].Position);
    }

    [Fact]
    public void 모르는_포지션과_티어는_미지정으로_들어간다()
    {
        var players = Paste("박민수 , 없는포지션 , 3티어");

        var p = Assert.Single(players);
        Assert.Equal("박민수", p.Name);
        Assert.Equal(Positions.Unset, p.Position);
        Assert.Equal(Tiers.Unset, p.Tier);
    }

    [Fact]
    public void 빈_줄은_건너뛴다()
    {
        Assert.Equal(2, Paste("홍길동\n\n김철수\n   \n").Count);
    }

    [Fact]
    public void 내용이_없으면_아무것도_추가되지_않는다()
    {
        var room = Empty();

        Assert.Equal(0, room.ImportPlayers("   "));
        Assert.Empty(room.Players);
    }

    // ── 엑셀에서 복사해 붙여넣는 경우 ────────────────────────────────────
    // 엑셀 클립보드는 탭으로 나뉘고 줄바꿈이 CRLF다.

    [Fact]
    public void 탭으로_나뉜_세_열을_받는다()
    {
        var players = Paste("홍길동\t타격대\t다이아몬드\r\n김철수\t감시자\t골드\r\n");

        Assert.Equal(2, players.Count);
        Assert.Equal("홍길동", players[0].Name);
        Assert.Equal("다이아몬드", players[0].Tier);
    }

    [Fact]
    public void 이름만_있는_한_열도_받는다()
    {
        Assert.Equal(3, Paste("홍길동\r\n김철수\r\n이영희\r\n").Count);
    }

    [Fact]
    public void 머리글_행은_건너뛴다()
    {
        var players = Paste("이름\t포지션\t티어\r\n홍길동\t타격대\r\n");

        Assert.Equal("홍길동", Assert.Single(players).Name);
    }

    [Fact]
    public void 머리글은_첫_줄에서_정확히_일치할_때만_걸러낸다()
    {
        Assert.Equal(2, Paste("홍길동\t타격대\r\n김철수\t감시자\r\n").Count);
        Assert.Equal("이름없는자", Assert.Single(Paste("이름없는자\t타격대\r\n")).Name);
    }

    [Fact]
    public void 앞뒤에_빈_열이_딸려와도_인식한다()
    {
        Assert.Equal("홍길동", Assert.Single(Paste("\t홍길동\t타격대\r\n")).Name);

        var trailing = Assert.Single(Paste("홍길동\t타격대\t다이아몬드\t\t\r\n"));
        Assert.Equal("다이아몬드", trailing.Tier);
    }

    [Fact]
    public void 탭이_있으면_쉼표는_이름의_일부다()
    {
        // 엑셀은 쉼표가 든 셀에 따옴표를 씌우지 않으므로, 쉼표까지 구분자로 쓰면 이름이 쪼개진다.
        Assert.Equal("홍길동, 별명", Assert.Single(Paste("홍길동, 별명\t타격대\r\n")).Name);
    }

    [Fact]
    public void 엑셀이_씌운_따옴표를_벗긴다()
    {
        Assert.Equal("홍길동", Assert.Single(Paste("\"홍길동\"\t타격대\r\n")).Name);
    }

    [Fact]
    public void 번호_열은_그대로_이름이_된다()
    {
        // 숫자로만 된 닉네임을 잃지 않으려고 일부러 손대지 않는다.
        var players = Paste("1\t홍길동\t타격대\r\n2\t김철수\r\n");

        Assert.Equal(["1", "2"], players.Select(p => p.Name));
        Assert.Equal("1004", Assert.Single(Paste("1004\t타격대\r\n")).Name);
    }

    // ── 중복 이름 ────────────────────────────────────────────────────────

    [Fact]
    public void 중복이_없으면_경고할_것이_없다()
    {
        var room = Empty();
        room.AddPlayer("홍길동");
        room.AddPlayer("김철수");

        Assert.Empty(room.DuplicatePlayerNames());
    }

    [Fact]
    public void 같은_이름을_대소문자_구분없이_잡아낸다()
    {
        var room = Empty();
        room.AddPlayer("홍길동");
        room.AddPlayer("홍길동");
        room.AddPlayer("KIM");
        room.AddPlayer("kim");

        Assert.Equal(2, room.DuplicatePlayerNames().Count);
    }

    [Fact]
    public void 중복_때문에_시작이_막히지는_않는다()
    {
        var room = TestRoom.Create(teams: 2, rounds: 1, players: 2);
        room.AddPlayer(room.Players[0].Name);

        Assert.NotEmpty(room.DuplicatePlayerNames());
        Assert.Null(room.ValidateForStart());
    }

    // ── 이름 없는 입력 ───────────────────────────────────────────────────

    [Fact]
    public void 이름이_비면_추가되지_않는다()
    {
        var room = Empty();

        Assert.Null(room.AddPlayer("   "));
        Assert.Empty(room.Players);
    }
}
