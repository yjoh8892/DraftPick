using System.Collections.Concurrent;
using System.Security.Cryptography;
using DraftPick.Models;

namespace DraftPick.Services;

/// <summary>
/// 살아 있는 드래프트 방들을 들고 있는 싱글턴. DB 없이 서버 메모리에만 둔다.
/// </summary>
public sealed class DraftRoomStore
{
    /// <summary>사람이 불러주기 쉽게 헷갈리는 글자(O/0, I/1)는 뺐다.</summary>
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private readonly ConcurrentDictionary<string, DraftRoom> _rooms = new(StringComparer.OrdinalIgnoreCase);

    public DraftRoom Create()
    {
        while (true)
        {
            var code = RandomCode(5);
            var room = new DraftRoom { Code = code, HostKey = RandomCode(12) };
            if (_rooms.TryAdd(code, room)) return room;
        }
    }

    public DraftRoom? Find(string? code) =>
        !string.IsNullOrWhiteSpace(code) && _rooms.TryGetValue(code.Trim(), out var room) ? room : null;

    public IReadOnlyCollection<DraftRoom> All => _rooms.Values.ToList();

    /// <summary>오래 방치된 방을 치운다.</summary>
    public int RemoveIdle(TimeSpan maxIdle)
    {
        var cutoff = DateTimeOffset.UtcNow - maxIdle;
        var removed = 0;
        foreach (var room in _rooms.Values)
        {
            if (room.LastActivityAt < cutoff && _rooms.TryRemove(room.Code, out _)) removed++;
        }
        return removed;
    }

    private static string RandomCode(int length)
    {
        var chars = new char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
        }
        return new string(chars);
    }
}
