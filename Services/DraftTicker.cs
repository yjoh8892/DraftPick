namespace DraftPick.Services;

/// <summary>
/// 1초마다 모든 방을 한 번씩 두드린다. 턴 타이머가 여기서 흐르고,
/// 방이 상태 변경을 알리면 Blazor 회선들이 각자 화면을 다시 그린다.
/// 클라이언트가 아니라 서버가 시계를 쥐고 있으므로 모두가 같은 초를 본다.
/// </summary>
public sealed class DraftTicker(DraftRoomStore store, ILogger<DraftTicker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan IdleRoomLifetime = TimeSpan.FromHours(6);
    private static readonly TimeSpan CleanupEvery = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        var lastCleanup = DateTimeOffset.UtcNow;

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (var room in store.All)
            {
                try
                {
                    room.Tick();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "방 {Code} 처리 중 오류", room.Code);
                }
            }

            if (DateTimeOffset.UtcNow - lastCleanup >= CleanupEvery)
            {
                lastCleanup = DateTimeOffset.UtcNow;
                var removed = store.RemoveIdle(IdleRoomLifetime);
                if (removed > 0) logger.LogInformation("방치된 방 {Count}개 정리", removed);
            }
        }
    }
}
