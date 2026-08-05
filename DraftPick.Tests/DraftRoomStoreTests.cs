using DraftPick.Models;
using DraftPick.Services;

namespace DraftPick.Tests;

public class DraftRoomStoreTests
{
    [Fact]
    public void 만든_방은_코드로_찾을_수_있다()
    {
        var store = new DraftRoomStore();

        var room = store.Create();

        Assert.Same(room, store.Find(room.Code));
    }

    [Fact]
    public void 방_코드는_대소문자를_가리지_않는다()
    {
        var store = new DraftRoomStore();
        var room = store.Create();

        Assert.Same(room, store.Find(room.Code.ToLowerInvariant()));
        Assert.Same(room, store.Find($"  {room.Code}  "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("없는코드")]
    public void 없는_방을_찾으면_null이다(string? code)
    {
        Assert.Null(new DraftRoomStore().Find(code));
    }

    [Fact]
    public void 방_코드는_헷갈리는_글자를_쓰지_않는다()
    {
        var store = new DraftRoomStore();

        // O/0, I/1 처럼 불러줄 때 헷갈리는 글자는 뺐다.
        for (var i = 0; i < 50; i++)
        {
            var code = store.Create().Code;

            Assert.Equal(5, code.Length);
            Assert.DoesNotContain(code, c => c is 'O' or '0' or 'I' or '1');
        }
    }

    [Fact]
    public void 방마다_다른_코드와_진행자_키를_받는다()
    {
        var store = new DraftRoomStore();

        var rooms = Enumerable.Range(0, 30).Select(_ => store.Create()).ToList();

        Assert.Equal(30, rooms.Select(r => r.Code).Distinct().Count());
        Assert.Equal(30, rooms.Select(r => r.HostKey).Distinct().Count());
    }

    [Fact]
    public void 방치된_방만_정리된다()
    {
        var store = new DraftRoomStore();
        var stale = store.Create();
        var fresh = store.Create();

        // 활동이 있으면 LastActivityAt이 갱신된다.
        fresh.AddTeam("살아있음");
        var removed = store.RemoveIdle(TimeSpan.Zero);

        Assert.Equal(2, removed);
        Assert.Null(store.Find(stale.Code));

        var keeper = store.Create();
        Assert.Equal(0, store.RemoveIdle(TimeSpan.FromHours(6)));
        Assert.NotNull(store.Find(keeper.Code));
    }

    [Fact]
    public void 여러_스레드가_동시에_방을_만들어도_코드가_겹치지_않는다()
    {
        var store = new DraftRoomStore();
        var rooms = new System.Collections.Concurrent.ConcurrentBag<string>();

        Parallel.For(0, 200, _ => rooms.Add(store.Create().Code));

        Assert.Equal(200, rooms.Distinct().Count());
        Assert.Equal(200, store.All.Count());
    }
}
