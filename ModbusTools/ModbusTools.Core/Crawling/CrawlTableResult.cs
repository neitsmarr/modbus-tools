using ModbusTools.Core.Protocol;

namespace ModbusTools.Core.Crawling;

/// <summary>
/// Per-address results of one table over the crawled range. Stored as compact arrays because a full-range crawl has
/// 65,536 addresses per table.
/// </summary>
public sealed class CrawlTableResult
{
    private readonly AddressStatus[] statuses;
    private readonly ushort[] values;
    private readonly byte[] exceptionCodes;
    private readonly float[] responseTimesMs;
    private readonly Dictionary<int, string> details = [];
    private readonly int[] statusCounts = new int[Enum.GetValues<AddressStatus>().Length];

    private int illegalFunctionCount;
    private int segmentsVersion = -1;
    private AddressSegment[] segments = [];

    public CrawlTableResult(CrawlTable table, AddressRange range)
    {
        Table = table;
        Range = range;
        statuses = new AddressStatus[range.Count];
        values = new ushort[range.Count];
        exceptionCodes = new byte[range.Count];
        responseTimesMs = new float[range.Count];
        statusCounts[(int)AddressStatus.NotChecked] = range.Count;
    }

    public CrawlTable Table { get; }

    public AddressRange Range { get; }

    /// <summary>Incremented whenever a result is recorded, so views can cache what they derive from it.</summary>
    public int Version { get; private set; }

    public int CheckedCount => Range.Count - CountOf(AddressStatus.NotChecked);

    public int CountOf(AddressStatus status) => statusCounts[(int)status];

    public TableSupport Support => this switch
    {
        _ when CountOf(AddressStatus.Valid) > 0 => TableSupport.Supported,
        _ when CountOf(AddressStatus.Invalid) + CountOf(AddressStatus.Exception) > illegalFunctionCount => TableSupport.NoValidAddresses,
        _ when illegalFunctionCount > 0 => TableSupport.NotSupported,
        _ when CheckedCount > 0 => TableSupport.NoAnswer,
        _ => TableSupport.NotChecked,
    };

    public AddressStatus GetStatus(int address) => statuses[IndexOf(address)];

    public AddressEntry GetEntry(int address)
    {
        var index = IndexOf(address);
        var status = statuses[index];
        var responseTime = responseTimesMs[index];
        return new AddressEntry(
            Table,
            (ushort)address,
            status,
            values[index],
            status is AddressStatus.Invalid or AddressStatus.Exception ? exceptionCodes[index] : null,
            status is AddressStatus.NotChecked || float.IsNaN(responseTime) ? null : TimeSpan.FromMilliseconds(responseTime),
            details.GetValueOrDefault(index));
    }

    /// <summary>Entries of all checked addresses in ascending address order.</summary>
    public IEnumerable<AddressEntry> GetCheckedEntries()
    {
        for (int address = Range.From; address <= Range.To; address++)
        {
            if (statuses[address - Range.From] != AddressStatus.NotChecked)
            {
                yield return GetEntry(address);
            }
        }
    }

    /// <summary>The range split into runs of equal status, in ascending order.</summary>
    public IReadOnlyList<AddressSegment> GetSegments()
    {
        if (segmentsVersion == Version)
        {
            return segments;
        }

        var runs = new List<AddressSegment>();
        var start = 0;
        for (var index = 1; index <= statuses.Length; index++)
        {
            if (index == statuses.Length || statuses[index] != statuses[start])
            {
                runs.Add(new AddressSegment(
                    new AddressRange((ushort)(Range.From + start), (ushort)(Range.From + index - 1)), statuses[start]));
                start = index;
            }
        }

        segments = runs.ToArray();
        segmentsVersion = Version;
        return segments;
    }

    /// <summary>Runs of consecutive valid addresses, e.g. 0-35, 100-149.</summary>
    public IReadOnlyList<AddressRange> GetValidRanges() =>
        GetSegments().Where(s => s.Status == AddressStatus.Valid).Select(s => s.Range).ToArray();

    /// <summary>
    /// Applies a completed read. A successful read marks each of its addresses valid and stores the values. A failed
    /// read of one address gives that address the failure's status. A failed read of several addresses changes no
    /// address, because the device does not say which of them caused the failure.
    /// </summary>
    public void Record(CrawlReadResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var read = result.Read;
        if (result.Table != Table || !Range.Contains(read.Address) || !Range.Contains(read.LastAddress))
        {
            throw new ArgumentException($"Read {read} of {result.Table} is outside {Table} {Range}.", nameof(result));
        }

        var attempt = result.Attempt;
        var classification = attempt.Classification;
        var responseTime = attempt.ResponseTime;
        var lateNote = attempt.LateBytes.IsEmpty ? null : $"{attempt.LateBytes.Length} late byte(s) after the response window.";

        if (attempt.Status == ProbeStatus.Ok)
        {
            for (var i = 0; i < read.Quantity; i++)
            {
                var value = Table.GetValue(attempt.Response.Span, i);
                Set(read.Address + i, AddressStatus.Valid, value, 0, responseTime, lateNote);
            }
        }
        else if (read.Quantity == 1)
        {
            switch (attempt.Status)
            {
                case ProbeStatus.Exception:
                    var code = classification.ExceptionCode.GetValueOrDefault();
                    var status = code == ModbusExceptionCodes.IllegalDataAddress ? AddressStatus.Invalid : AddressStatus.Exception;
                    Set(read.Address, status, 0, code, responseTime, lateNote);
                    break;
                case ProbeStatus.NoResponse:
                    Set(read.Address, AddressStatus.NoResponse, 0, 0, null, lateNote);
                    break;
                default:
                    Set(read.Address, AddressStatus.CommError, 0, 0, responseTime, Join(classification.Detail, lateNote));
                    break;
            }
        }

        Version++;
    }

    private void Set(int address, AddressStatus status, ushort value, byte exceptionCode, TimeSpan? responseTime, string? detail)
    {
        var index = IndexOf(address);
        statusCounts[(int)statuses[index]]--;
        if (IsIllegalFunction(index))
        {
            illegalFunctionCount--;
        }

        statuses[index] = status;
        values[index] = value;
        exceptionCodes[index] = exceptionCode;
        responseTimesMs[index] = responseTime is TimeSpan time ? (float)time.TotalMilliseconds : float.NaN;
        if (detail is null)
        {
            details.Remove(index);
        }
        else
        {
            details[index] = detail;
        }

        statusCounts[(int)status]++;
        if (IsIllegalFunction(index))
        {
            illegalFunctionCount++;
        }
    }

    private bool IsIllegalFunction(int index) =>
        statuses[index] == AddressStatus.Exception && exceptionCodes[index] == ModbusExceptionCodes.IllegalFunction;

    private int IndexOf(int address)
    {
        if (!Range.Contains(address))
        {
            throw new ArgumentOutOfRangeException(nameof(address), address, $"Address is outside {Range}.");
        }

        return address - Range.From;
    }

    private static string? Join(string? first, string? second) =>
        first is null ? second : second is null ? first : $"{first} {second}";
}
