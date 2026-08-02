namespace DraftPick.Models;

/// <summary>지명권을 가진 팀(팀장).</summary>
public sealed class Team
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Captain { get; set; } = "";
    public string Color { get; set; } = TeamColors.Palette[0];
}

public static class TeamColors
{
    public static readonly string[] Palette =
    [
        "#4f7cff", "#ff5c8a", "#2fc08b", "#f2a33c",
        "#a06bff", "#00b8d4", "#e0553f", "#7c8b9a"
    ];
}
