namespace Tindy.Infrastructure.Storage;

internal static class BoundedStorageContent
{
    // A small, bounded buffer makes streams with unknown length safe for both Local and S3.
    // Higher-level image/PDF content verification belongs to STEP 03/04/05.
    public static async Task<MemoryStream> ReadAsync(Stream source, long limitBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead) throw new ArgumentException("Content stream must be readable.", nameof(source));

        var buffer = new byte[64 * 1024];
        var result = new MemoryStream();
        try
        {
            while (true)
            {
                int read = await source.ReadAsync(buffer.AsMemory(), cancellationToken);
                if (read == 0) break;
                if (result.Length > limitBytes - read)
                    throw new InvalidDataException($"Storage object exceeds the {limitBytes}-byte limit.");
                await result.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            if (result.Length == 0) throw new InvalidDataException("Storage object cannot be empty.");
            result.Position = 0;
            return result;
        }
        catch
        {
            await result.DisposeAsync();
            throw;
        }
    }
}
