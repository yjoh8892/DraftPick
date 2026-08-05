using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace DraftPick.Models;

/// <summary>지명 순서 방식.</summary>
public enum DraftOrderMode
{
    /// <summary>1→2→3→3→2→1. 뒤 순번의 불리함을 줄인다.</summary>
    Snake,

    /// <summary>1→2→3→1→2→3. 매 라운드 같은 순서.</summary>
    Sequential,
}

public static class DraftOrderModes
{
    /// <summary>화면과 결과 텍스트에 쓰는 짧은 이름. 방식을 추가하면 여기만 고치면 된다.</summary>
    public static string Label(this DraftOrderMode mode) =>
        mode == DraftOrderMode.Snake ? "스네이크" : "순차";

    public static string Pattern(this DraftOrderMode mode) =>
        mode == DraftOrderMode.Snake ? "1→2→3 / 3→2→1" : "1→2→3 / 1→2→3";
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

    public const int MinRounds = 1;
    public const int MaxRounds = 15;
    public const int MaxTurnSeconds = 600;

    private DraftOrderMode _orderMode = DraftOrderMode.Snake;
    private int _rounds = 5;
    private int _turnSeconds = 60;

    public string Title { get; set; } = "내전 드래프트";

    // 아래 세 값은 TotalPicks와 턴 타이머를 결정한다. 시작한 뒤에 바뀌면 진행 중인 드래프트가
    // 어긋나므로, 화면의 min/max 속성에 기대지 않고 세터가 직접 막고 범위도 좁힌다.

    public DraftOrderMode OrderMode
    {
        get => _orderMode;
        set { if (Status == RoomStatus.Setup) _orderMode = value; }
    }

    public int Rounds
    {
        get => _rounds;
        set { if (Status == RoomStatus.Setup) _rounds = Math.Clamp(value, MinRounds, MaxRounds); }
    }

    /// <summary>턴당 제한시간(초). 0이면 무제한.</summary>
    public int TurnSeconds
    {
        get => _turnSeconds;
        set { if (Status == RoomStatus.Setup) _turnSeconds = Math.Clamp(value, 0, MaxTurnSeconds); }
    }

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

    // 지금 이 방을 보고 있는 화면들. 팀 자리에 앉은 사람과 그 외(진행자·관전자)를 따로 센다.
    // 여러 회선이 동시에 드나들므로 _gate가 아니라 스레드 안전한 자료구조를 쓴다.
    private readonly ConcurrentDictionary<Guid, int> _seatedByTeam = new();
    private int _otherViewers;

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

    /// <summary>
    /// 모든 팀의 로스터를 한 번의 순회로 구한다. 화면이 팀마다 <see cref="RosterOf"/>를 부르면
    /// 전체 선수를 팀 수만큼 다시 훑게 되는데, 이 화면은 1초에 한 번씩 다시 그려진다.
    /// </summary>
    public Dictionary<Guid, List<Player>> RostersByTeam()
    {
        var rosters = _teams.ToDictionary(t => t.Id, _ => new List<Player>());

        foreach (var player in _players.Where(p => p.IsDrafted).OrderBy(p => p.PickNumber))
        {
            if (rosters.TryGetValue(player.DraftedBy!.Value, out var roster)) roster.Add(player);
        }
        return rosters;
    }

    /// <summary>진행자 키가 맞는지. 누가 진행자인지는 키를 가진 이 객체만 판단할 수 있다.</summary>
    public bool IsHost(string? hostKey) => !string.IsNullOrEmpty(hostKey) && hostKey == HostKey;

    public bool CanUndo => PickIndex > 0;

    /// <summary>
    /// 시간은 다 됐는데 자동 지명이 꺼져 있어 진행자를 기다리는 상태.
    /// 이때 <see cref="TurnEndsAt"/>은 "무제한"일 때와 똑같이 null이라, 화면이 두 필드로 되짚지 않도록 여기서 이름을 준다.
    /// </summary>
    public bool AwaitingHostAfterTimeout =>
        Status == RoomStatus.Running && TurnSeconds > 0 && TurnEndsAt is null;

    /// <summary>지명이 막히는 이유. 막을 이유가 없으면 null. <see cref="Pick"/>과 같은 규칙을 쓴다.</summary>
    public string? WhyCannotPick(Guid actingTeamId, string? hostKey)
    {
        if (Status == RoomStatus.Paused) return "일시정지 중입니다.";
        if (Status != RoomStatus.Running) return "진행 중인 드래프트가 아닙니다.";

        var current = TeamAtPick(PickIndex);
        if (current is null) return "남은 픽이 없습니다.";
        if (!IsHost(hostKey) && current.Id != actingTeamId) return "지금은 당신의 차례가 아닙니다.";

        return null;
    }

    public bool CanPick(Guid actingTeamId, string? hostKey) => WhyCannotPick(actingTeamId, hostKey) is null;

    // ── 접속 현황 ────────────────────────────────────────────────────────────

    /// <summary>화면 하나가 자리에 앉는다. teamId가 null이면 진행자나 관전자.</summary>
    public void TakeSeat(Guid? teamId)
    {
        if (teamId is { } id) _seatedByTeam.AddOrUpdate(id, 1, (_, n) => n + 1);
        else Interlocked.Increment(ref _otherViewers);

        Touch();
    }

    public void LeaveSeat(Guid? teamId)
    {
        if (teamId is { } id)
        {
            // 같은 팀으로 두 명이 들어와 있을 수 있으므로 세어서 뺀다.
            if (_seatedByTeam.AddOrUpdate(id, 0, (_, n) => n - 1) <= 0) _seatedByTeam.TryRemove(id, out _);
        }
        else
        {
            Interlocked.Decrement(ref _otherViewers);
        }
        Touch();
    }

    public bool IsTeamConnected(Guid teamId) => _seatedByTeam.TryGetValue(teamId, out var n) && n > 0;

    /// <summary>자리를 잡은 팀 수. 진행자가 시작 전에 확인하는 값이다.</summary>
    public int ConnectedTeamCount => _teams.Count(t => IsTeamConnected(t.Id));

    /// <summary>이 방을 보고 있는 화면 수.</summary>
    public int ViewerCount => _seatedByTeam.Values.Sum() + Volatile.Read(ref _otherViewers);

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

    /// <summary>설정 단계가 아니면 아무것도 하지 않고 null을 준다(다른 설정 메서드와 같은 관례).</summary>
    public Team? AddTeam(string name = "", string captain = "")
    {
        Team team;
        lock (_gate)
        {
            if (Status != RoomStatus.Setup) return null;
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
            foreach (var p in _players.Where(p => p.DraftedBy == teamId)) p.Release();
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

    /// <summary>지명 순서를 무작위로 다시 뽑는다. 색은 팀에 붙어 있으므로 순서만 섞인다.</summary>
    public void ShuffleTeams()
    {
        lock (_gate)
        {
            if (Status != RoomStatus.Setup) return;

            for (var i = _teams.Count - 1; i > 0; i--)
            {
                var j = RandomNumberGenerator.GetInt32(i + 1);
                (_teams[i], _teams[j]) = (_teams[j], _teams[i]);
            }
        }
        Touch();
    }

    /// <summary>두 번 이상 등록된 이름. 시작을 막지는 않고 진행자에게 알려만 준다.</summary>
    public IReadOnlyList<string> DuplicatePlayerNames() =>
        _players
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

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

    /// <summary>
    /// 한 줄에 한 명씩. "이름", "이름,포지션", "이름,포지션,티어" 모두 받는다.
    /// 엑셀에서 복사하면 탭으로 나뉜 CRLF 텍스트가 들어오는데 그것도 그대로 받는다.
    /// </summary>
    public int ImportPlayers(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;

        var added = 0;
        lock (_gate)
        {
            var isFirstRow = true;
            foreach (var raw in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var cells = SplitRow(raw);
                if (cells[0].Length == 0) continue;

                // 표를 통째로 복사하면 머리글이 딸려온다. 첫 줄에 한해 걸러낸다.
                if (isFirstRow)
                {
                    isFirstRow = false;
                    if (LooksLikeHeader(cells)) continue;
                }

                var position = cells.Length > 1 && Positions.All.Contains(cells[1]) ? cells[1] : Positions.Unset;
                var tier = Tiers.Normalize(cells.Length > 2 ? cells[2] : null);

                _players.Add(new Player { Name = cells[0], Position = position, Tier = tier });
                added++;
            }
        }
        if (added > 0) Touch();
        return added;
    }

    /// <summary>
    /// 엑셀 클립보드는 탭 구분이다. 탭이 있으면 쉼표는 구분자가 아니라 값의 일부로 본다
    /// ("홍길동, 별명" 같은 이름이 쪼개지지 않도록).
    /// </summary>
    private static string[] SplitRow(string row)
    {
        var separator = row.Contains('\t') ? '\t' : ',';
        var cells = row.Split(separator, StringSplitOptions.TrimEntries);

        for (var i = 0; i < cells.Length; i++) cells[i] = Unquote(cells[i]);
        return cells;
    }

    /// <summary>셀에 줄바꿈이나 따옴표가 있으면 엑셀이 "..."로 감싸고 내부 따옴표는 ""로 이스케이프한다.</summary>
    private static string Unquote(string cell) =>
        cell.Length >= 2 && cell[0] == '"' && cell[^1] == '"'
            ? cell[1..^1].Replace("\"\"", "\"")
            : cell;

    private static readonly string[] NameHeaders = ["이름", "성명", "닉네임", "선수", "선수명", "name", "player"];
    private static readonly string[] PositionHeaders = ["포지션", "역할", "역할군", "position", "role"];

    private static bool LooksLikeHeader(string[] cells) =>
        NameHeaders.Contains(cells[0], StringComparer.OrdinalIgnoreCase)
        || (cells.Length > 1 && PositionHeaders.Contains(cells[1], StringComparer.OrdinalIgnoreCase));

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
            ResetTurnClockNoLock();
        }
        Touch();
        return null;
    }

    /// <summary>
    /// 선수 지명. 성공하면 null, 실패하면 사유.
    /// 진행자 여부는 호출자가 알려주는 게 아니라 <paramref name="hostKey"/>를 받아 여기서 직접 판정한다.
    /// </summary>
    public string? Pick(Guid playerId, Guid actingTeamId, string? hostKey)
    {
        lock (_gate)
        {
            if (WhyCannotPick(actingTeamId, hostKey) is { } reason) return reason;

            var player = _players.FirstOrDefault(p => p.Id == playerId);
            if (player is null) return "없는 선수입니다.";
            if (player.IsDrafted) return "이미 지명된 선수입니다.";

            CommitPickNoLock(player, TeamAtPick(PickIndex)!);
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

            last.Release();
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
            if (_pausedRemaining is { } left) TurnEndsAt = DateTimeOffset.UtcNow + left;
            else ResetTurnClockNoLock();
            _pausedRemaining = null;
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
            foreach (var p in _players) p.Release();
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
        player.AssignTo(team.Id, PickIndex + 1);
        AdvanceNoLock();
    }

    private void AdvanceNoLock()
    {
        PickIndex++;
        if (PickIndex >= TotalPicks)
        {
            Status = RoomStatus.Finished;
            TurnEndsAt = null;
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
