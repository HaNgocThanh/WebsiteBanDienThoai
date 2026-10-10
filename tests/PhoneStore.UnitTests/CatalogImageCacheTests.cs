using PhoneStore.Api.Services.Catalog;

namespace PhoneStore.UnitTests;

public sealed class CatalogImageCacheTests
{
    private sealed class Store : ICatalogImageStore
    {
        public bool IsConfigured => true;
        public int Reads;
        public byte[]? Bytes = [1, 2, 3];
        public bool Fail;
        public TaskCompletionSource? Gate;
        public Task SaveAsync(string name, byte[] bytes, CancellationToken ct) { Bytes = bytes; return Task.CompletedTask; }
        public async Task<byte[]?> ReadAsync(string name, CancellationToken ct)
        {
            Interlocked.Increment(ref Reads);
            var result = Bytes;
            if (Gate is not null) await Gate.Task;
            if (Fail) throw new IOException("Synthetic provider failure");
            return result;
        }
        public Task DeleteAsync(string name) { Bytes = null; return Task.CompletedTask; }
    }
    [Fact]
    public async Task RepeatedReadsReuseBytesAndSizesHaveSeparateEntries()
    {
        var source = new Store(); using var cache = new CachedCatalogImageStore(source);
        await cache.ReadAsync("synthetic", default); await cache.ReadAsync("synthetic", default);
        await cache.ReadSizedAsync("synthetic", CatalogImageSize.Card, default); await cache.ReadSizedAsync("synthetic", CatalogImageSize.Card, default);
        Assert.Equal(2, source.Reads);
        await cache.SaveAsync("synthetic", [4], default);
        Assert.Equal(new byte[] { 4 }, await cache.ReadAsync("synthetic", default));
        await cache.DeleteAsync("synthetic"); Assert.Null(await cache.ReadAsync("synthetic", default));
    }
    [Fact]
    public async Task ParallelReadersShareFetchAndCancelingOneDoesNotCancelOthers()
    {
        var source = new Store { Gate = new(TaskCreationOptions.RunContinuationsAsynchronously) }; using var cache = new CachedCatalogImageStore(source); using var canceled = new CancellationTokenSource();
        var first = cache.ReadAsync("synthetic", canceled.Token); var second = cache.ReadAsync("synthetic", default);
        canceled.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.Equal(1, source.Reads); source.Gate.SetResult();
        Assert.Equal(source.Bytes, await second); Assert.Equal(source.Bytes, await cache.ReadAsync("synthetic", default)); Assert.Equal(1, source.Reads);
    }
    [Fact]
    public async Task MissingAndFailedResultsAreNeverCached()
    {
        var source = new Store { Bytes = null }; using var cache = new CachedCatalogImageStore(source);
        Assert.Null(await cache.ReadAsync("synthetic", default)); Assert.Null(await cache.ReadAsync("synthetic", default)); Assert.Equal(2, source.Reads);
        source.Fail = true; await Assert.ThrowsAsync<IOException>(() => cache.ReadAsync("synthetic", default));
        source.Fail = false; source.Bytes = [5]; Assert.Equal(source.Bytes, await cache.ReadAsync("synthetic", default)); Assert.Equal(4, source.Reads);
    }
    [Fact]
    public async Task OversizedResultIsReturnedWithoutCaching()
    {
        var source = new Store { Bytes = new byte[33 * 1024 * 1024] }; using var cache = new CachedCatalogImageStore(source);
        await cache.ReadAsync("synthetic", default); await cache.ReadAsync("synthetic", default); Assert.Equal(2, source.Reads);
    }
    [Fact]
    public async Task DistinctImageDownloadsAreLimitedToFourAtOnce()
    {
        var source = new Store { Gate = new(TaskCreationOptions.RunContinuationsAsynchronously) }; using var cache = new CachedCatalogImageStore(source);
        var requests = Enumerable.Range(0, 8).Select(index => cache.ReadAsync("synthetic-" + index, default)).ToArray();
        Assert.Equal(4, source.Reads); source.Gate.SetResult(); await Task.WhenAll(requests); Assert.Equal(8, source.Reads);
    }
    [Fact]
    public async Task DeleteWhileFetchIsPendingPreventsRefillingDeletedEntry()
    {
        var source = new Store { Gate = new(TaskCreationOptions.RunContinuationsAsynchronously) }; using var cache = new CachedCatalogImageStore(source);
        var pending = cache.ReadAsync("synthetic", default); await cache.DeleteAsync("synthetic"); source.Gate.SetResult(); await pending;
        source.Gate = null; source.Bytes = [9]; Assert.Equal(source.Bytes, await cache.ReadAsync("synthetic", default)); Assert.Equal(2, source.Reads);
    }
}
