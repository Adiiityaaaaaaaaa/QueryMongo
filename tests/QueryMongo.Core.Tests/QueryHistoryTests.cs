using QueryMongo.Core.Connections;
using QueryMongo.Core.Models;
using Xunit;

namespace QueryMongo.Core.Tests;

public sealed class QueryHistoryTests : IDisposable
{
    private const string Ns = "shop.orders";

    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"querymongo-history-{Guid.NewGuid():N}", "queries.json");

    private static QuerySpec Spec(string filter) => new() { Filter = filter, Limit = 50 };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RecordedQueryComesBack()
    {
        using var store = new QueryHistoryStore(_path);

        await store.RecordAsync(Ns, Spec("{ status: 'open' }"), Ct);

        var only = Assert.Single(await store.LoadAsync(Ns, Ct));
        Assert.Equal("{ status: 'open' }", only.Spec.Filter);
        Assert.False(only.IsFavorite);
    }

    [Fact]
    public async Task IdenticalQueryIsNotDuplicated()
    {
        using var store = new QueryHistoryStore(_path);

        await store.RecordAsync(Ns, Spec("{ a: 1 }"), Ct);
        await store.RecordAsync(Ns, Spec("{ a: 1 }"), Ct);
        await store.RecordAsync(Ns, Spec("{ a: 1 }"), Ct);

        Assert.Single(await store.LoadAsync(Ns, Ct));
    }

    [Fact]
    public async Task WhitespaceDoesNotCreateASecondEntry()
    {
        using var store = new QueryHistoryStore(_path);

        await store.RecordAsync(Ns, Spec("{ a: 1 }"), Ct);
        await store.RecordAsync(Ns, Spec("{  a:   1  }"), Ct);

        Assert.Single(await store.LoadAsync(Ns, Ct));
    }

    [Fact]
    public async Task EmptyQueryIsNotRecorded()
    {
        using var store = new QueryHistoryStore(_path);

        await store.RecordAsync(Ns, Spec("{}"), Ct);
        await store.RecordAsync(Ns, Spec("   "), Ct);

        Assert.Empty(await store.LoadAsync(Ns, Ct));
    }

    [Fact]
    public async Task FavoritesSortBeforeHistory()
    {
        using var store = new QueryHistoryStore(_path);

        await store.RecordAsync(Ns, Spec("{ recent: 1 }"), Ct);
        await store.SaveFavoriteAsync(Ns, Spec("{ saved: 1 }"), "Saved one", Ct);

        var all = await store.LoadAsync(Ns, Ct);

        Assert.Equal(2, all.Count);
        Assert.True(all[0].IsFavorite);
        Assert.Equal("Saved one", all[0].Name);
    }

    [Fact]
    public async Task HistoryIsScopedToItsNamespace()
    {
        using var store = new QueryHistoryStore(_path);

        await store.RecordAsync(Ns, Spec("{ a: 1 }"), Ct);
        await store.RecordAsync("other.collection", Spec("{ b: 2 }"), Ct);

        Assert.Single(await store.LoadAsync(Ns, Ct));
        Assert.Equal(2, (await store.LoadAsync(null, Ct)).Count);
    }

    [Fact]
    public async Task ClearingHistoryKeepsFavorites()
    {
        using var store = new QueryHistoryStore(_path);

        await store.RecordAsync(Ns, Spec("{ a: 1 }"), Ct);
        await store.SaveFavoriteAsync(Ns, Spec("{ b: 2 }"), "Keep me", Ct);

        await store.ClearHistoryAsync(Ns, Ct);

        var remaining = Assert.Single(await store.LoadAsync(Ns, Ct));
        Assert.Equal("Keep me", remaining.Name);
    }

    [Fact]
    public async Task HistoryIsCappedPerNamespace()
    {
        using var store = new QueryHistoryStore(_path);

        // The cap is 30; writing well past it must not grow the file without bound.
        for (var i = 0; i < 45; i++)
            await store.RecordAsync(Ns, Spec($"{{ n: {i} }}"), Ct);

        var all = await store.LoadAsync(Ns, Ct);
        Assert.Equal(30, all.Count);
    }

    [Fact]
    public async Task DeleteRemovesOneEntry()
    {
        using var store = new QueryHistoryStore(_path);
        await store.RecordAsync(Ns, Spec("{ a: 1 }"), Ct);

        var entry = Assert.Single(await store.LoadAsync(Ns, Ct));
        await store.DeleteAsync(entry.Id, Ct);

        Assert.Empty(await store.LoadAsync(Ns, Ct));
    }

    [Fact]
    public async Task SummaryMentionsSortAndProjection()
    {
        using var store = new QueryHistoryStore(_path);

        await store.RecordAsync(Ns, new QuerySpec
        {
            Filter = "{ a: 1 }",
            Sort = "{ b: -1 }",
            Projection = "{ c: 1 }"
        }, Ct);

        var entry = Assert.Single(await store.LoadAsync(Ns, Ct));

        Assert.Contains("sort", entry.Summary, StringComparison.Ordinal);
        Assert.Contains("project", entry.Summary, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        var dir = Path.GetDirectoryName(_path)!;
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }
}
