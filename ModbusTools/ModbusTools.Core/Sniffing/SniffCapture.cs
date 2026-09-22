namespace ModbusTools.Core.Sniffing;

/// <summary>
/// Everything one capture has received: the raw chunks, the decoded log and the statistics. It is fed what the port
/// delivers (bytes, silence, line errors) with the time each arrived, and does no I/O itself, so a recorded capture can
/// be fed through it the same way.
/// </summary>
public sealed class SniffCapture
{
    private readonly IFrameSplitter splitter;
    private readonly TransactionMatcher matcher;
    private readonly BoundedLog<SniffEntry> entries;
    private readonly Queue<CaptureChunk> chunks = new();
    private long chunkCount;
    private long entryCount;

    public SniffCapture(SniffOptions options, IFrameSplitter splitter)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(splitter);
        Options = options;
        this.splitter = splitter;
        entries = new BoundedLog<SniffEntry>(options.Capacity);
        matcher = new TransactionMatcher(options.ResponseTimeout, Statistics);
    }

    public SniffOptions Options { get; }

    public SniffStatistics Statistics { get; } = new();

    /// <summary>The newest <see cref="SniffOptions.Capacity"/> log entries, oldest first.</summary>
    public IReadOnlyList<SniffEntry> Entries => entries;

    /// <summary>Log entries dropped to stay within <see cref="SniffOptions.Capacity"/>.</summary>
    public long DroppedEntries => entries.Dropped;

    /// <summary>
    /// Received chunks and line errors, oldest first, from the chunk that holds the start of the oldest kept entry.
    /// Chunks that only fed dropped entries are dropped with them.
    /// </summary>
    public IReadOnlyCollection<CaptureChunk> Chunks => chunks;

    /// <summary>Time of the latest event fed in, from the start of the capture.</summary>
    public TimeSpan Elapsed { get; private set; }

    /// <summary>Changes whenever an entry is added or a request's outcome is decided.</summary>
    public long Version => entryCount + matcher.Settled;

    /// <summary>Bytes received in one chunk at <paramref name="time"/>.</summary>
    public void Receive(ReadOnlySpan<byte> bytes, TimeSpan time)
    {
        if (bytes.IsEmpty)
        {
            return;
        }

        var chunk = AddChunk(time, CaptureChunkKind.Data, bytes.ToArray());
        Statistics.RecordBytes(bytes.Length);
        Record(splitter.Push(bytes, chunk.Stamp));
        matcher.Expire(time);
    }

    /// <summary>The line has been silent up to <paramref name="time"/>: a frame in progress cannot continue.</summary>
    public void Silence(TimeSpan time)
    {
        Elapsed = time;
        Record(splitter.Flush());
        matcher.Expire(time);
    }

    /// <summary>The port reported a line error at <paramref name="time"/>, which breaks the stream there.</summary>
    public void LineError(TimeSpan time)
    {
        Record(splitter.Flush());
        var chunk = AddChunk(time, CaptureChunkKind.LineError, ReadOnlyMemory<byte>.Empty);
        matcher.NoteDamage(time);
        var entry = new SniffEntry(++entryCount, SniffEntryKind.LineError, [], chunk.Stamp, chunk.Stamp, summary: null);
        Statistics.RecordEntry(entry);
        Add(entry);
    }

    /// <summary>The capture stops at <paramref name="time"/>: decodes what is held back and settles what it can.</summary>
    public void End(TimeSpan time)
    {
        Elapsed = time;
        Record(splitter.Flush());
        matcher.End(time);
    }

    private CaptureChunk AddChunk(TimeSpan time, CaptureChunkKind kind, ReadOnlyMemory<byte> bytes)
    {
        Elapsed = time;
        var chunk = new CaptureChunk(++chunkCount, time, kind, bytes);
        chunks.Enqueue(chunk);
        return chunk;
    }

    private void Record(IReadOnlyList<SplitFrame> frames)
    {
        foreach (var frame in frames)
        {
            SniffEntry entry;
            if (frame.Kind == SplitFrameKind.Garbled)
            {
                matcher.NoteDamage(frame.First.Time);
                entry = new SniffEntry(++entryCount, SniffEntryKind.Garbled, frame.Bytes, frame.First, frame.Last, summary: null);
                Statistics.RecordEntry(entry);
            }
            else
            {
                entry = matcher.Accept(frame, ++entryCount);
            }

            Add(entry);
        }
    }

    private void Add(SniffEntry entry)
    {
        entries.Add(entry);
        if (entries.Dropped > 0)
        {
            var needed = entries[0].First.Chunk;
            while (chunks.TryPeek(out var chunk) && chunk.Number < needed)
            {
                chunks.Dequeue();
            }
        }
    }
}
