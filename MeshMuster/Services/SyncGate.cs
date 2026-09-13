namespace MeshMuster.Services;

/// <summary>
/// Lets one release sync run at a time, process-wide.
/// </summary>
/// <remarks>
/// The poller and the Sync button share a database but not a DbContext. Overlapping, they
/// each delete the same stale assets and the loser's DELETE finds nothing, which EF reports
/// as a concurrency failure. The second GitHub fetch is free; the etag is already fresh.
/// </remarks>
public class SyncGate : IDisposable
{
    /// <summary>
    /// Wait for the gate; dispose the lease to release it.
    /// </summary>
    /// <param name="ct"></param>
    public async Task<IDisposable> EnterAsync(CancellationToken ct = default)
    {
        await this.Semaphore.WaitAsync(ct).ConfigureAwait(false);
        return new Lease(this.Semaphore);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        this.Semaphore.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// The semaphore that limits the sync to one runner.
    /// </summary>
    private readonly SemaphoreSlim Semaphore = new(1, 1);

    /// <summary>
    /// A held gate; releasing twice is a no-op.
    /// </summary>
    private class Lease : IDisposable
    {
        /// <summary>
        /// Initializes a new instance of the Lease class.
        /// </summary>
        /// <param name="semaphore"></param>
        public Lease(SemaphoreSlim semaphore)
        {
            this.Semaphore = semaphore;
        }

        /// <inheritdoc />
        public void Dispose() => Interlocked.Exchange(ref this.Semaphore, null)?.Release();

        /// <summary>
        /// The semaphore to release, nulled once it has been.
        /// </summary>
        private SemaphoreSlim? Semaphore;
    }
}
