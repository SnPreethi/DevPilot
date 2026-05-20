using System;
using System.Collections.Generic;

namespace DevPilot.AI;

/// <summary>
/// Stores KV cache tensors across decode steps. Values may be DenseTensor&lt;float&gt;
/// for FP32 models or DenseTensor&lt;Float16&gt; for FP16 models, or OrtValue for direct GPU memory bindings.
/// </summary>
public sealed class KvCacheState : IDisposable
{
    public Dictionary<string, object> Values { get; } = new();

    public void Dispose()
    {
        foreach (var value in Values.Values)
        {
            if (value is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
        Values.Clear();
    }
}