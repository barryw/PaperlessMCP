namespace PaperlessMCP.Client;

/// <summary>
/// A document's file as Paperless-ngx is serving it, held open so callers can stream it.
/// </summary>
/// <remarks>
/// <para>
/// The response is fetched with <see cref="HttpCompletionOption.ResponseHeadersRead"/>, so the body
/// has not been read yet when this object is handed out. Nothing is buffered until a caller asks
/// for bytes, and a caller writing to disk never holds the whole document in memory.
/// </para>
/// <para>
/// Reading the body happens after <c>HttpClient</c> has returned the response, so it is no longer
/// covered by <see cref="HttpClient.Timeout"/>. Every read here is therefore bounded by
/// <see cref="StallTimeout"/>, re-armed after each chunk: a transfer that keeps making progress is
/// never cut off however large the document is, while one that stalls fails instead of pinning a
/// request forever.
/// </para>
/// </remarks>
public sealed class DocumentFileResponse : IAsyncDisposable
{
    private const int CopyBufferSize = 81920;

    private readonly HttpResponseMessage _response;
    private readonly Stream _content;

    internal DocumentFileResponse(HttpResponseMessage response, Stream content, TimeSpan stallTimeout)
    {
        _response = response;
        _content = content;
        StallTimeout = stallTimeout;
    }

    /// <summary>MIME type Paperless reported, when it sent one.</summary>
    public string? ContentType => _response.Content.Headers.ContentType?.MediaType;

    /// <summary>
    /// File name from the response's <c>Content-Disposition</c>, when present.
    /// </summary>
    /// <remarks>
    /// This is the authoritative name for the bytes actually being served, which is not
    /// necessarily the document's original file name: the archived version of a scan is a PDF.
    /// </remarks>
    public string? SuggestedFileName
    {
        get
        {
            var disposition = _response.Content.Headers.ContentDisposition;
            var name = disposition?.FileNameStar ?? disposition?.FileName;
            return name?.Trim('"');
        }
    }

    /// <summary>Length from <c>Content-Length</c>, when Paperless sent one.</summary>
    public long? ContentLength => _response.Content.Headers.ContentLength;

    /// <summary>How long a single read may stall before the transfer is abandoned.</summary>
    public TimeSpan StallTimeout { get; }

    /// <summary>
    /// Copies the file to <paramref name="destination"/> and returns the number of bytes written.
    /// </summary>
    public async Task<long> CopyToAsync(Stream destination, CancellationToken cancellationToken = default)
    {
        var buffer = new byte[CopyBufferSize];
        long total = 0;

        using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ArmStallTimeout(stall);

        int read;
        while ((read = await _content.ReadAsync(buffer, stall.Token).ConfigureAwait(false)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), stall.Token).ConfigureAwait(false);
            total += read;
            ArmStallTimeout(stall);
        }

        return total;
    }

    /// <summary>
    /// Reads at most <paramref name="maxBytes"/> bytes. <c>Exceeded</c> is true when the file is
    /// larger than that, in which case no bytes are returned: a caller that asked for the whole
    /// file cannot use a prefix of it.
    /// </summary>
    /// <remarks>
    /// Reads one byte past the limit rather than trusting <c>Content-Length</c>, so a missing or
    /// wrong header cannot pull an unbounded body into memory just to have it rejected.
    /// </remarks>
    public async Task<(byte[] Bytes, bool Exceeded)> ReadAtMostAsync(
        int maxBytes,
        CancellationToken cancellationToken = default)
    {
        var buffer = new byte[maxBytes + 1];
        var filled = 0;

        using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ArmStallTimeout(stall);

        while (filled < buffer.Length)
        {
            var read = await _content.ReadAsync(buffer.AsMemory(filled), stall.Token).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            filled += read;
            ArmStallTimeout(stall);
        }

        return filled > maxBytes ? ([], true) : (buffer[..filled], false);
    }

    /// <summary>
    /// (Re)starts the stall timer. Calling <see cref="CancellationTokenSource.CancelAfter(TimeSpan)"/>
    /// again replaces the pending due time, which is what makes this a stall timeout rather than a
    /// total-transfer deadline.
    /// </summary>
    private void ArmStallTimeout(CancellationTokenSource stall)
    {
        if (StallTimeout != Timeout.InfiniteTimeSpan)
        {
            stall.CancelAfter(StallTimeout);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _content.DisposeAsync().ConfigureAwait(false);
        _response.Dispose();
    }
}
