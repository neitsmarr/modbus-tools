using System.Collections;

namespace ModbusTools.Core.Sniffing;

/// <summary>A list that keeps the newest <see cref="Capacity"/> items, dropping the oldest when a new one arrives.</summary>
public sealed class BoundedLog<T> : IReadOnlyList<T>
{
    private readonly T[] items;
    private int oldest;

    public BoundedLog(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        items = new T[capacity];
    }

    public int Capacity => items.Length;

    public int Count { get; private set; }

    /// <summary>Items dropped to make room so far.</summary>
    public long Dropped { get; private set; }

    /// <summary>Items in order, oldest first.</summary>
    public T this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
            return items[(oldest + index) % items.Length];
        }
    }

    public void Add(T item)
    {
        if (Count < items.Length)
        {
            items[(oldest + Count) % items.Length] = item;
            Count++;
            return;
        }

        items[oldest] = item;
        oldest = (oldest + 1) % items.Length;
        Dropped++;
    }

    public IEnumerator<T> GetEnumerator()
    {
        for (var i = 0; i < Count; i++)
        {
            yield return this[i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
