namespace DraftPick.Models;

/// <summary>드래프트 대상 선수 한 명.</summary>
public sealed class Player
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Position { get; set; } = Positions.Unset;
    public string Tier { get; set; } = "";

    /// <summary>지명한 팀. null이면 아직 풀에 남아 있는 선수.</summary>
    public Guid? DraftedBy { get; private set; }

    /// <summary>몇 번째 지명으로 뽑혔는지(1부터).</summary>
    public int? PickNumber { get; private set; }

    public bool IsDrafted => DraftedBy is not null;

    // 지명 결과는 DraftRoom이 락 안에서만 건드린다. 그래서 setter 대신 이 두 메서드만 열어둔다.

    internal void AssignTo(Guid teamId, int pickNumber)
    {
        DraftedBy = teamId;
        PickNumber = pickNumber;
    }

    internal void Release()
    {
        DraftedBy = null;
        PickNumber = null;
    }
}

/// <summary>발로란트 역할군. 게임이 바뀌면 이 목록만 갈아끼우면 된다.</summary>
public static class Positions
{
    public const string Unset = "미지정";

    public static readonly string[] All = [Unset, "타격대", "척후대", "전략가", "감시자"];
}

/// <summary>발로란트 랭크. 낮은 티어부터 나열하므로 인덱스가 곧 실력 순위다.</summary>
public static class Tiers
{
    public const string Unset = "";

    public static readonly string[] All =
        [Unset, "아이언", "브론즈", "실버", "골드", "플래티넘", "다이아몬드", "초월", "불멸", "레디언트"];

    /// <summary>목록에서의 위치. 모르는 값과 미지정은 0이라 정렬하면 항상 맨 뒤로 간다.</summary>
    public static int RankOf(string? tier) => Math.Max(0, Array.IndexOf(All, tier));

    /// <summary>붙여넣기로 들어온 값을 목록에 맞춘다. "다이아"처럼 줄여 쓴 것도 받아준다.</summary>
    public static string Normalize(string? raw)
    {
        var value = raw?.Trim();
        if (string.IsNullOrEmpty(value)) return Unset;
        if (All.Contains(value)) return value;

        var prefixed = All.Skip(1).FirstOrDefault(t => t.StartsWith(value, StringComparison.Ordinal));
        return prefixed ?? Unset;
    }
}
