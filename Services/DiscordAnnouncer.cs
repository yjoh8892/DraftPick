using System.Net.Http.Json;

namespace DraftPick.Services;

/// <summary>
/// 드래프트 결과를 디스코드 채널에 올린다.
/// 내전 참가자 대부분은 드래프트를 실시간으로 보지 않으므로, 결과가 채널에 남는 것이
/// 이 앱이 바깥과 이어지는 유일한 통로다.
/// </summary>
public sealed class DiscordAnnouncer(IHttpClientFactory factory, ILogger<DiscordAnnouncer> logger)
{
    /// <summary>디스코드 한 메시지 한도는 2000자다. 여유를 두고 자른다.</summary>
    private const int MaxMessageLength = 1900;

    /// <summary>
    /// 웹훅 주소가 맞는지 확인한다. 서버가 대신 요청을 보내주는 자리라 아무 주소나 받으면
    /// 내부망을 찔러보는 통로가 된다. 그래서 디스코드 웹훅 형태만 통과시킨다.
    /// </summary>
    public static bool IsWebhookUrl(string? url) =>
        Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.Host is "discord.com" or "discordapp.com" or "ptb.discord.com" or "canary.discord.com"
        && uri.AbsolutePath.StartsWith("/api/webhooks/", StringComparison.Ordinal);

    /// <summary>보낸 뒤 문제가 없으면 null, 있으면 사람에게 보여줄 사유를 준다.</summary>
    public async Task<string?> PostAsync(string webhookUrl, string content, CancellationToken cancellationToken = default)
    {
        if (!IsWebhookUrl(webhookUrl)) return "디스코드 웹훅 주소가 아닙니다.";
        if (string.IsNullOrWhiteSpace(content)) return "보낼 내용이 없습니다.";

        var client = factory.CreateClient(nameof(DiscordAnnouncer));

        foreach (var chunk in Split(content))
        {
            try
            {
                var response = await client.PostAsJsonAsync(webhookUrl.Trim(), new { content = chunk }, cancellationToken);
                if (response.IsSuccessStatusCode) continue;

                logger.LogWarning("디스코드 게시 실패: {Status}", response.StatusCode);
                return $"디스코드가 요청을 거절했습니다({(int)response.StatusCode}). 웹훅 주소를 확인해주세요.";
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                logger.LogWarning(ex, "디스코드 게시 중 통신 오류");
                return "디스코드에 연결하지 못했습니다.";
            }
        }

        return null;
    }

    /// <summary>줄 단위로 잘라 메시지 한도를 넘지 않게 한다. 한 줄이 한도를 넘으면 그 줄만 잘라 보낸다.</summary>
    internal static IEnumerable<string> Split(string content)
    {
        var chunk = new System.Text.StringBuilder();

        foreach (var line in content.Split('\n'))
        {
            var piece = line.Length <= MaxMessageLength ? line : line[..MaxMessageLength];

            if (chunk.Length > 0 && chunk.Length + 1 + piece.Length > MaxMessageLength)
            {
                yield return chunk.ToString();
                chunk.Clear();
            }

            if (chunk.Length > 0) chunk.Append('\n');
            chunk.Append(piece);
        }

        if (chunk.Length > 0) yield return chunk.ToString();
    }
}
