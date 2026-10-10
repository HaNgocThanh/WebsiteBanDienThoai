using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;

namespace PhoneStore.Api.Services.Catalog;

// Cache bytes only. CatalogService still verifies SQL visibility for every HTTP request.
public sealed class CachedCatalogImageStore(ICatalogImageStore inner) : ICatalogImageStore, IDisposable
{
    private const long Capacity = 32 * 1024 * 1024;
    private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = Capacity });
    private readonly ConcurrentDictionary<string, Flight> flights = new();
    private readonly SemaphoreSlim downloads = new(4);
    private sealed class Flight
    {
        public int Invalidated;
        public Lazy<Task<CatalogImageContent?>> Work { get; }
        public Flight(Func<Flight, Task<CatalogImageContent?>> load) { Work = new(() => load(this), true); }
    }
    public bool IsConfigured => inner.IsConfigured;
    public bool UsesCloudinary => inner.UsesCloudinary;
    private static string Key(string name, CatalogImageSize size) => name + ":" + size;
    private void Invalidate(string name)
    {
        foreach (var size in Enum.GetValues<CatalogImageSize>())
        {
            var key = Key(name, size);
            if (flights.TryGetValue(key, out var flight)) { lock (flight) { flight.Invalidated = 1; } }
            cache.Remove(key);
        }
    }
    public async Task SaveAsync(string name, byte[] bytes, CancellationToken ct)
    {
        Invalidate(name);
        try { await inner.SaveAsync(name, bytes, ct); }
        finally { Invalidate(name); }
    }
    public async Task DeleteAsync(string name)
    {
        Invalidate(name);
        try { await inner.DeleteAsync(name); }
        finally { Invalidate(name); }
    }
    public async Task<byte[]?> ReadAsync(string name, CancellationToken ct)
        => (await ReadSizedAsync(name, CatalogImageSize.Original, ct))?.Bytes;
    public async Task<CatalogImageContent?> ReadSizedAsync(string name, CatalogImageSize size, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var key = Key(name, size);
        if (cache.TryGetValue<CatalogImageContent>(key, out var hit)) return hit;
        var flight = flights.GetOrAdd(key, _ => new Flight(async current =>
        {
            await downloads.WaitAsync(CancellationToken.None);
            try
            {
                if (cache.TryGetValue<CatalogImageContent>(key, out var secondHit)) return secondHit;
                // A canceled browser does not abort a fetch shared with other readers.
                var result = await inner.ReadSizedAsync(name, size, CancellationToken.None);
                lock (current)
                {
                    if (result is not null && result.Bytes.LongLength <= Capacity && current.Invalidated == 0)
                        cache.Set(key, result, new MemoryCacheEntryOptions { Size = Math.Max(1024, result.Bytes.LongLength), AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) });
                }
                return result;
            }
            finally { downloads.Release(); }
        }));
        var task = flight.Work.Value;
        _ = task.ContinueWith(completed => { _ = completed.Exception; flights.TryRemove(new KeyValuePair<string, Flight>(key, flight)); }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return await task.WaitAsync(ct);
    }
    public void Dispose() { cache.Dispose(); if (inner is IDisposable disposable) disposable.Dispose(); }
}
