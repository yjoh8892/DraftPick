namespace DraftPick.Models;

/// <summary>지명 순서 방식.</summary>
public enum DraftOrderMode
{
    /// <summary>1→2→3→3→2→1. 뒤 순번의 불리함을 줄인다.</summary>
    Snake,

    /// <summary>1→2→3→1→2→3. 매 라운드 같은 순서.</summary>
    Sequential,
}

public enum RoomStatus
{
    Setup,
    Running,
    Paused,
    Finished,
}

/// <summary>
/// 드래프트 한 판의 모든 상태. 서버 메모리에만 존재하며 이 객체가 유일한 진실 원본이다.
/// 상태를 바꾸는 모든 경로는 <see cref="_gate"/> 안에서 처리하고, 끝난 뒤 <see cref="Changed"/>로 알린다.
/// </summary>
public sealed class DraftRoom
{
    private readonly object _gate = new();
    private readonly List<Team> _teams = [];
    private readonly List<Player> _players = [];

    public required string Code { get; init; }

    /// <summary>진행자만 아는 키. 쿼리스트링으로 들고 다닌다.</summary>
    public required string HostKey { get; init; }

    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastActivityAt { get; private set; } = DateTimeOffset.UtcNow;

    public string Title { get; set; } = "내전 드래프트";
    public DraftOrderMode OrderMode { get; set; } = DraftOrderMode.Snake;
    public int Rounds { get; set; } = 5;

    /// <summary>턴당 제한시간(초). 0이면 무제한.</summary>
    public int TurnSeconds { get; set; } = 60;

    /// <summary>시간이 다 되면 남은 선수 중 맨 위를 자동 지명할지. false면 타이머만 멈추고 기다린다.</summary>
    public bool AutoPickOnTimeout { get; set; } = true;

    public IReadOnlyList<Team> Teams => _teams;
    public IReadOnlyList<Player> Players => _players;

    public RoomStatus Status { get; private set; } = RoomStatus.Setup;

    /// <summary>다음에 진행할 픽의 0-based 순번.</summary>
    public int PickIndex { get; private set; }

    /// <summary>현재 턴 마감 시각. 무제한이거나 일시정지면 null.</summary>
    public DateTimeOffset? TurnEndsAt { get; private set; }

    private TimeSpan? _pausedRemaining;

    /// <summary>상태가 바뀔 때마다 발생. 백그라운드 스레드에서도 호출되므로 UI는 InvokeAsync로 받아야 한다.</summary>
    public event Action? Changed;

    // ── 파생 값 ──────────────────────────────────────────────────────────────

    public int TotalPicks => _teams.Count * Rounds;

    public bool IsLive => Status is RoomStatus.Running or RoomStatus.Paused;

    /// <summary>현재 라운드(1부터). 드래프트 중이 아니면 0.</summary>
    public int CurrentRound => IsLive && _teams.Count > 0 ? PickIndex / _teams.Count + 1 : 0;

    public Team? CurrentTeam => TeamAtPick(PickIndex);

    public IEnumerable<Player> AvailablePlayers => _players.Where(p => !p.IsDrafted);

    public IEnumerable<Player> RosterOf(Guid teamId) =>
        _players.Where(p => p.DraftedBy == teamId).OrderBy(p => p.PickNumber);

    /// <summary>남은 초. 무제한이면 null, 이미 지났으면 0.</summary>
    public int? SecondsLeft
    {
        get
        {
            if (_pausedRemaining is { } paused) return (int)Math.Ceiling(paused.TotalSeconds);
            if (TurnEndsAt is not { } ends) return null;
            var left = ends - DateTimeOffset.UtcNow;
            return left <= TimeSpan.Zero ? 0 : (int)Math.Ceiling(left.TotalSeconds);
        }
    }

    /// <summary>idx번째 픽을 가진 팀. 범위를 벗어나면 null.</summary>
    public Team? TeamAtPick(int idx)
    {
        var n = _teams.Count;
        if (n == 0 || idx < 0 || idx >= TotalPicks) return null;

        var round = idx / n;
        var slot = idx % n;
        if (OrderMode == DraftOrderMode.Snake && round % 2 == 1) slot = n - 1 - slot;
        return _teams[slot];
    }

    /// <summary>다음 픽 순서 미리보기.</summary>
    public IEnumerable<(int PickNumber, Team Team)> UpcomingPicks(int count)
    {
        for (var i = PickIndex; i < Math.Min(PickIndex + count, TotalPicks); i++)
        {
            if (TeamAtPick(i) is { } t) yield return (i + 1, t);
        }
    }

    // ── 설정 단계 ────────────────────────────────────────────────────────────

    public Team AddTeam(string name = "", string captain = "")
    {
        Team team;
        lock (_gate)
        {
            if (Status != RoomStatus.Setup) throw new InvalidOperationException("설정 단계에서만 팀을 추가할 수 있습니다.");
            team = new Team
            {
                Name = string.IsNullOrWhiteSpace(name) ? $"팀 {_teams.Count + 1}" : name.Trim(),
                Captain = captain.Trim(),
                Color = TeamColors.Palette[_teams.Count % TeamColors.Palette.Length],
            };
            _teams.Add(team);
        }
        Touch();
        return team;
    }

    public void RemoveTeam(Guid teamId)
    {
        lock (_gate)
        {
            if (Status != RoomStatus.Setup) return;
            _teams.RemoveAll(t => t.Id == teamId);
            foreach (var p in _players.Where(p => p.DraftedBy == teamId))
            {
                p.DraftedBy = null;
                p.PickNumber = null;
            }
        }
        Touch();
    }

    public void MoveTeam(Guid teamId, int delta)
    {
        lock (_gate)
        {
            if (Status != RoomStatus.Setup) return;
            var i = _teams.FindIndex(t => t.Id == teamId);
            var j = i + delta;
            if (i < 0 || j < 0 || j >= _teams.Count) return;
            (_teams[i], _teams[j]) = (_teams[j], _teams[i]);
        }
        Touch();
    }

    public Player? AddPlayer(string name, string position = Positions.Unset, string tier = "")
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        Player player;
        lock (_gate)
        {
            player = new Player
            {
                Name = name.Trim(),
                Position = Positions.All.Contains(position) ? position : Positions.Unset,
                Tier = Tiers.Normalize(tier),
            };
            _players.Add(player);
        }
        Touch();
        return player;
    }

    /// <summary>한 줄에 한 명씩. "이름", "이름,포지션", "이름,포지션,티어" 모두 받는다.</summary>
    public int ImportPlayers(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;

        var added = 0;
        lock (_gate)
        {
            foreach (var raw in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var parts = raw.Split([',', '\t'], StringSplitOptions.TrimEntries);
                var name = parts[0];
                if (name.Length == 0) continue;

                var position = parts.Length > 1 && Positions.All.Contains(parts[1]) ? parts[1] : Positions.Unset;
                var tier = Tiers.Normalize(parts.Length > 2 ? parts[2] : null);

                _players.Add(new Player { Name = name, Position = position, Tier = tier });
                added++;
            }
        }
        if (added > 0) Touch();
        return added;
    }

    public void RemovePlayer(Guid playerId)
    {
        lock (_gate)
        {
            if (Status != RoomStatus.Setup) return;
            _players.RemoveAll(p => p.Id == playerId);
        }
        Touch();
    }

    public void ClearPlayers()
    {
        lock (_gate)
        {
            if (Status != RoomStatus.Setup) return;
            _players.Clear();
        }
        Touch();
    }

    /// <summary>높은 티어부터 정렬. 자동 지명은 이 순서의 맨 위를 고르므로 시작 전에 정리해두면 좋다.</summary>
    public void SortPlayersByTier()
    {
        lock (_gate)
        {
            if (Status != RoomStatus.Setup) return;
            var sorted = _players
                .OrderByDescending(p => Tiers.RankOf(p.Tier))
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            _players.Clear();
            _players.AddRange(sorted);
        }
        Touch();
    }

    /// <summary>시작 가능한지 검사. 문제가 없으면 null.</summary>
    public string? ValidateForStart()
    {
        lock (_gate)
        {
            if (_teams.Count < 2) return "팀이 최소 2개는 있어야 합니다.";
            if (Rounds < 1) return "라운드 수는 1 이상이어야 합니다.";
            if (_teams.Any(t => string.IsNullOrWhiteSpace(t.Name))) return "이름이 비어 있는 팀이 있습니다.";

            var needed = _teams.Count * Rounds;
            if (_players.Count < needed)
                return $"선수가 부족합니다. {_teams.Count}팀 × {Rounds}라운드 = {needed}명이 필요한데 {_players.Count}명뿐입니다.";

            return null;
        }
    }

    // ── 진행 ─────────────────────────────────────────────────────────────────

    public string? Start()
    {
        if (ValidateForStart() is { } error) return error;

        lock (_gate)
        {
            if (Status != RoomStatus.Setup) return "이미 시작된 드래프트입니다.";
            Status = RoomStatus.Running;
            PickIndex = 0;
            _pausedRemaining = null;
            ResetTurnClockNoLock();
        }
        Touch();
        return null;
    }

    /// <summary>선수 지명. 성공하면 null, 실패하면 사유.</summary>
    public string? Pick(Guid playerId, Guid actingTeamId, bool byHost)
    {
        lock (_gate)
        {
            if (Status == RoomStatus.Paused) return "일시정지 중입니다.";
            if (Status != RoomStatus.Running) return "진행 중인 드래프트가 아닙니다.";

            var current = TeamAtPick(PickIndex);
            if (current is null) return "남은 픽이 없습니다.";
            if (!byHost && current.Id != actingTeamId) return "지금은 당신의 차례가 아닙니다.";

            var player = _players.FirstOrDefault(p => p.Id == playerId);
            if (player is null) return "없는 선수입니다.";
            if (player.IsDrafted) return "이미 지명된 선수입니다.";

            CommitPickNoLock(player, current);
        }
        Touch();
        return null;
    }

    /// <summary>마지막 픽 취소. 진행자만 호출한다.</summary>
    public string? UndoLastPick()
    {
        lock (_gate)
        {
            if (PickIndex == 0) return "되돌릴 픽이 없습니다.";

            var last = _players.FirstOrDefault(p => p.PickNumber == PickIndex);
            if (last is null) return "마지막 픽을 찾을 수 없습니다.";

            last.DraftedBy = null;
            last.PickNumber = null;
            PickIndex--;

            if (Status == RoomStatus.Finished) Status = RoomStatus.Running;
            _pausedRemaining = null;
            ResetTurnClockNoLock();
        }
        Touch();
        return null;
    }

    /// <summary>지명 없이 턴만 넘긴다. 그 팀은 이번 라운드 선수를 못 받는다.</summary>
    public void SkipTurn()
    {
        lock (_gate)
        {
            if (Status != RoomStatus.Running) return;
            AdvanceNoLock();
        }
        Touch();
    }

    public void Pause()
    {
        lock (_gate)
        {
            if (Status != RoomStatus.Running) return;
            Status = RoomStatus.Paused;
            if (TurnEndsAt is { } ends)
            {
                var left = ends - DateTimeOffset.UtcNow;
                _pausedRemaining = left > TimeSpan.Zero ? left : TimeSpan.Zero;
            }
            TurnEndsAt = null;
        }
        Touch();
    }

    public void Resume()
    {
        lock (_gate)
        {
            if (Status != RoomStatus.Paused) return;
            Status = RoomStatus.Running;
            TurnEndsAt = _pausedRemaining is { } left ? DateTimeOffset.UtcNow + left : null;
            _pausedRemaining = null;
            if (TurnEndsAt is null && TurnSeconds > 0) ResetTurnClockNoLock();
        }
        Touch();
    }

    /// <summary>결과를 지우고 설정 단계로 되돌린다. 팀과 선수 명단은 유지.</summary>
    public void ResetToSetup()
    {
        lock (_gate)
        {
            Status = RoomStatus.Setup;
            PickIndex = 0;
            TurnEndsAt = null;
            _pausedRemaining = null;
            foreach (var p in _players)
            {
                p.DraftedBy = null;
                p.PickNumber = null;
            }
        }
        Touch();
    }

    /// <summary>1초마다 서버가 호출. 시간이 다 된 턴을 처리하고, 진행 중이면 카운트다운을 위해 알린다.</summary>
    public void Tick()
    {
        if (Status != RoomStatus.Running) return;

        var timedOut = false;
        lock (_gate)
        {
            if (Status != RoomStatus.Running) return;
            if (TurnEndsAt is { } ends && DateTimeOffset.UtcNow >= ends)
            {
                if (AutoPickOnTimeout)
                {
                    var current = TeamAtPick(PickIndex);
                    var best = _players.FirstOrDefault(p => !p.IsDrafted);
                    if (current is not null && best is not null) CommitPickNoLock(best, current);
                    else AdvanceNoLock();
                }
                else
                {
                    // 타이머만 멈춰 세우고 진행자의 처리를 기다린다.
                    TurnEndsAt = null;
                }
                timedOut = true;
            }
        }

        if (timedOut) Touch();
        else if (TurnEndsAt is not null) Changed?.Invoke();   // 카운트다운 갱신용. 활동 시각은 건드리지 않는다.
    }

    // ── 내부 ─────────────────────────────────────────────────────────────────

    private void CommitPickNoLock(Player player, Team team)
    {
        player.DraftedBy = team.Id;
        player.PickNumber = PickIndex + 1;
        AdvanceNoLock();
    }

    private void AdvanceNoLock()
    {
        PickIndex++;
        if (PickIndex >= TotalPicks)
        {
            Status = RoomStatus.Finished;
            TurnEndsAt = null;
            _pausedRemaining = null;
        }
        else
        {
            ResetTurnClockNoLock();
        }
    }

    private void ResetTurnClockNoLock() =>
        TurnEndsAt = TurnSeconds > 0 ? DateTimeOffset.UtcNow.AddSeconds(TurnSeconds) : null;

    private void Touch()
    {
        LastActivityAt = DateTimeOffset.UtcNow;
        Changed?.Invoke();
    }

    /// <summary>진행자가 설정을 직접 바꿨을 때 화면을 갱신시키기 위한 용도.</summary>
    public void NotifyChanged() => Touch();
}
