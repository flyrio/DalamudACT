using System;
using System.Collections.Generic;

namespace ActMcpBridge;

internal sealed class FixedSizeLogBuffer
{
    private readonly int capacity;
    private readonly Queue<string> buffer;
    private readonly object gate = new();

    public FixedSizeLogBuffer(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        this.capacity = capacity;
        buffer = new Queue<string>(capacity);
    }

    public void Add(string line)
    {
        if (line == null) return;

        var stamped = $"{DateTime.Now:HH:mm:ss} {line}";
        lock (gate)
        {
            while (buffer.Count >= capacity)
                buffer.Dequeue();
            buffer.Enqueue(stamped);
        }
    }

    public string[] Tail(int count)
    {
        lock (gate)
        {
            if (count <= 0 || buffer.Count == 0)
                return Array.Empty<string>();

            var all = buffer.ToArray();
            if (all.Length <= count)
                return all;

            var result = new string[count];
            Array.Copy(all, all.Length - count, result, 0, count);
            return result;
        }
    }
}

