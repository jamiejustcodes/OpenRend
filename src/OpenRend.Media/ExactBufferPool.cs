using System;
using System.Collections.Concurrent;

namespace OpenRend.Media;

/// <summary>
/// Thread-safe, bounded exact-size buffer pool for video frames and audio chunks.
/// Guarantees that rented buffers have Length == requested size, preserving strict
/// buffer size invariants across DSP, effect pipelines, and native compositor calls
/// while eliminating LOH churn and garbage collection pauses.
/// </summary>
internal static class ExactBufferPool
{
    private static readonly ConcurrentDictionary<int, ConcurrentStack<byte[]>> BytePools = new();
    private static readonly ConcurrentDictionary<int, ConcurrentStack<float[]>> FloatPools = new();
    private const int MaxBuffersPerBucket = 8;

    public static byte[] RentBytes(int size)
    {
        if (size <= 0) return Array.Empty<byte>();
        var stack = BytePools.GetOrAdd(size, static _ => new ConcurrentStack<byte[]>());
        if (stack.TryPop(out var buffer))
        {
            return buffer;
        }
        return new byte[size];
    }

    public static void ReturnBytes(byte[]? buffer)
    {
        if (buffer == null || buffer.Length == 0) return;
        var stack = BytePools.GetOrAdd(buffer.Length, static _ => new ConcurrentStack<byte[]>());
        if (stack.Count < MaxBuffersPerBucket)
        {
            stack.Push(buffer);
        }
    }

    public static float[] RentFloats(int size)
    {
        if (size <= 0) return Array.Empty<float>();
        var stack = FloatPools.GetOrAdd(size, static _ => new ConcurrentStack<float[]>());
        if (stack.TryPop(out var buffer))
        {
            return buffer;
        }
        return new float[size];
    }

    public static void ReturnFloats(float[]? buffer)
    {
        if (buffer == null || buffer.Length == 0) return;
        var stack = FloatPools.GetOrAdd(buffer.Length, static _ => new ConcurrentStack<float[]>());
        if (stack.Count < MaxBuffersPerBucket)
        {
            stack.Push(buffer);
        }
    }
}

