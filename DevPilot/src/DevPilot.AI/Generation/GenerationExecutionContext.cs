using System;
using System.Collections.Generic;
using Microsoft.ML.OnnxRuntime;
using DevPilot.Contracts;

namespace DevPilot.AI.Generation;

/// <summary>
/// Manages high-performance, persistent unmanaged memory, OrtValue pooling, 
/// and OrtIoBinding reuse for an entire LLM token generation session.
/// </summary>
public sealed class GenerationExecutionContext : IDisposable
{
    private bool _isDisposed;

    public InferenceSession Session { get; }
    public OrtIoBinding IoBinding { get; }
    public ExecutionProviderKind ProviderKind { get; }

    // Persistent CPU array buffers
    public readonly long[] InputIdsBuffer = new long[1];
    public readonly long[] PositionIdsBuffer = new long[1];
    public readonly long[] AttentionMaskBuffer = new long[4096];
    public readonly float[] LogitsBuffer;

    // Persistent OrtValue references
    public OrtValue InputIdsOrtValue { get; private set; } = null!;
    public OrtValue PositionIdsOrtValue { get; private set; } = null!;

    // Profiling statistics
    public int StepsExecuted { get; set; }
    public int OrtValueReuseCount { get; set; }
    public int IoBindingReuseCount { get; set; }
    public long InitialGen0Collections { get; }
    public long InitialGen1Collections { get; }
    public long InitialGen2Collections { get; }

    public GenerationExecutionContext(InferenceSession session, int vocabSize, ExecutionProviderKind providerKind)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        ProviderKind = providerKind;
        LogitsBuffer = new float[vocabSize];
        IoBinding = session.CreateIoBinding();

        // Capture initial GC state for allocation profiling
        InitialGen0Collections = GC.CollectionCount(0);
        InitialGen1Collections = GC.CollectionCount(1);
        InitialGen2Collections = GC.CollectionCount(2);

        // Pre-fill attention mask buffer with 1s
        Array.Fill(AttentionMaskBuffer, 1L);

        InitializePersistentOrtValues();
        BindStaticTensors();
    }

    private void InitializePersistentOrtValues()
    {
        // Pinned memory OrtValues pointing to our stable arrays
        InputIdsOrtValue = OrtValue.CreateTensorValueFromMemory(InputIdsBuffer, [1L, 1L]);
        PositionIdsOrtValue = OrtValue.CreateTensorValueFromMemory(PositionIdsBuffer, [1L, 1L]);
    }

    private void BindStaticTensors()
    {
        // 1. Bind persistent inputs once
        IoBinding.BindInput("input_ids", InputIdsOrtValue);
        
        if (Session.InputMetadata.ContainsKey("position_ids"))
        {
            IoBinding.BindInput("position_ids", PositionIdsOrtValue);
        }

        // 2. Bind static outputs once
        IoBinding.BindOutputToDevice("logits", OrtMemoryInfo.DefaultInstance);
    }

    public void Dispose()
    {
        if (_isDisposed) return;

        InputIdsOrtValue?.Dispose();
        PositionIdsOrtValue?.Dispose();
        IoBinding?.Dispose();

        _isDisposed = true;
    }
}
