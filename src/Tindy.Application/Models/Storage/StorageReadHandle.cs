namespace Tindy.Application.Models.Storage;

// A GET response may own network resources in addition to the content stream.
// Callers must use: await using var handle = await storage.OpenReadAsync(...);
public sealed class StorageReadHandle : IAsyncDisposable, IDisposable
{
    private readonly IDisposable? _responseOwner;
    private bool _disposed;

    public StorageReadHandle(Stream content, IDisposable? responseOwner = null)
    {
        Content = content ?? throw new ArgumentNullException(nameof(content));
        _responseOwner = responseOwner;
    }

    public Stream Content { get; }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try { await Content.DisposeAsync(); }
        finally { _responseOwner?.Dispose(); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { Content.Dispose(); }
        finally { _responseOwner?.Dispose(); }
    }
}
