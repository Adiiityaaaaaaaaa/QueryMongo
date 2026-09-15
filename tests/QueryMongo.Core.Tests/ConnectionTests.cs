using QueryMongo.Core.Connections;
using Xunit;

namespace QueryMongo.Core.Tests;

public class ConnectionProfileTests
{
    [Fact]
    public void RedactHidesThePassword()
    {
        const string uri = "mongodb://alice:hunter2@cluster.example.com:27017/app";

        var redacted = ConnectionProfile.Redact(uri);

        Assert.DoesNotContain("hunter2", redacted, StringComparison.Ordinal);
        Assert.Contains("alice", redacted, StringComparison.Ordinal);
        Assert.Contains("cluster.example.com", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactLeavesCredentiallessUriAlone()
    {
        const string uri = "mongodb://localhost:27017";

        Assert.Equal(uri, ConnectionProfile.Redact(uri));
    }

    [Fact]
    public void RedactHandlesAtSignInHostWithoutCredentials()
    {
        // No colon before the '@' means there is no password to hide.
        const string uri = "mongodb+srv://cluster0.abcd.mongodb.net";

        Assert.Equal(uri, ConnectionProfile.Redact(uri));
    }

    [Fact]
    public void DeriveNameUsesTheHost()
    {
        var name = ConnectionProfile.DeriveName("mongodb://db.internal:27017");

        Assert.Equal("db.internal", name);
    }

    [Fact]
    public void DeriveNameFallsBackOnGarbage()
    {
        Assert.Equal("New connection", ConnectionProfile.DeriveName("not a uri"));
    }

    [Fact]
    public void CreateTrimsAndKeepsTheGivenName()
    {
        var profile = ConnectionProfile.Create("  Staging  ", " mongodb://localhost:27017 ");

        Assert.Equal("Staging", profile.Name);
        Assert.Equal("mongodb://localhost:27017", profile.ConnectionString);
    }
}

public sealed class FileConnectionStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"querymongo-tests-{Guid.NewGuid():N}", "connections.json");

    [Fact]
    public async Task SavedConnectionRoundTrips()
    {
        using var store = new FileConnectionStore(_path);
        var profile = ConnectionProfile.Create("Local", "mongodb://localhost:27017");

        await store.SaveAsync(profile, TestContext.Current.CancellationToken);
        var loaded = await store.LoadAsync(TestContext.Current.CancellationToken);

        var only = Assert.Single(loaded);
        Assert.Equal(profile.Id, only.Id);
        Assert.Equal("mongodb://localhost:27017", only.ConnectionString);
    }

    [Fact]
    public async Task ConnectionStringIsNotStoredInClearText()
    {
        using var store = new FileConnectionStore(_path);
        await store.SaveAsync(ConnectionProfile.Create("Secret", "mongodb://bob:s3cret@host:27017"), TestContext.Current.CancellationToken);

        var onDisk = await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken);

        Assert.DoesNotContain("s3cret", onDisk, StringComparison.Ordinal);
        Assert.DoesNotContain("mongodb://", onDisk, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SavingTwiceUpdatesInPlace()
    {
        using var store = new FileConnectionStore(_path);
        var profile = ConnectionProfile.Create("Local", "mongodb://localhost:27017");

        await store.SaveAsync(profile, TestContext.Current.CancellationToken);
        await store.SaveAsync(profile with { Name = "Renamed" }, TestContext.Current.CancellationToken);

        var loaded = await store.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Renamed", Assert.Single(loaded).Name);
    }

    [Fact]
    public async Task DeleteRemovesTheProfile()
    {
        using var store = new FileConnectionStore(_path);
        var profile = ConnectionProfile.Create("Local", "mongodb://localhost:27017");

        await store.SaveAsync(profile, TestContext.Current.CancellationToken);
        await store.DeleteAsync(profile.Id, TestContext.Current.CancellationToken);

        Assert.Empty(await store.LoadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MissingFileLoadsAsEmpty()
    {
        using var store = new FileConnectionStore(_path);

        Assert.Empty(await store.LoadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CorruptFileDoesNotBlockStartup()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await File.WriteAllTextAsync(_path, "{ this is not json", TestContext.Current.CancellationToken);

        using var store = new FileConnectionStore(_path);

        Assert.Empty(await store.LoadAsync(TestContext.Current.CancellationToken));
    }

    public void Dispose()
    {
        var dir = Path.GetDirectoryName(_path)!;
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }
}
