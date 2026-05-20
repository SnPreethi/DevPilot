using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DevPilot.Contracts;
using Microsoft.Extensions.Logging;

namespace DevPilot.Core.Productization;

public sealed class ProductModelManager : IProductModelManager
{
    private readonly ISettingsManager _settingsManager;
    private readonly ILogger<ProductModelManager> _logger;
    
    // Track downloads in progress
    private readonly ConcurrentDictionary<string, ModelDownloadProgress> _activeDownloads = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _downloadTokens = new();

    public ProductModelManager(ISettingsManager settingsManager, ILogger<ProductModelManager> logger)
    {
        _settingsManager = settingsManager;
        _logger = logger;
    }

    public IEnumerable<ModelDownloadProgress> GetModelsStatus()
    {
        var settings = _settingsManager.GetSettings();
        var models = new List<ModelDownloadProgress>();

        // 1. LLM Model
        var llmId = settings.ActiveLlmModel;
        var llmPath = Path.Combine(settings.ModelStoragePath, llmId, "model.onnx");
        models.Add(GetModelProgress(llmId, llmPath, 3_800_000_000)); // ~3.8 GB

        // 2. Embedding Model
        var embId = settings.ActiveEmbeddingModel;
        var embPath = Path.Combine(settings.ModelStoragePath, embId, "model.onnx");
        models.Add(GetModelProgress(embId, embPath, 120_000_000)); // ~120 MB

        return models;
    }

    private ModelDownloadProgress GetModelProgress(string modelId, string filePath, long totalBytes)
    {
        if (_activeDownloads.TryGetValue(modelId, out var active))
        {
            return active;
        }

        if (File.Exists(filePath))
        {
            var size = new FileInfo(filePath).Length;
            return new ModelDownloadProgress(
                ModelId: modelId,
                Status: ModelStatus.Ready,
                BytesReceived: size,
                TotalBytes: totalBytes,
                Percentage: 100.0,
                DownloadSpeedEstimate: "0 KB/s"
            );
        }

        return new ModelDownloadProgress(
            ModelId: modelId,
            Status: ModelStatus.Missing,
            BytesReceived: 0,
            TotalBytes: totalBytes,
            Percentage: 0.0,
            DownloadSpeedEstimate: "N/A"
        );
    }

    public Task StartDownloadAsync(string modelId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting download flow for model {ModelId}.", modelId);

        if (_activeDownloads.TryGetValue(modelId, out var active) && active.Status == ModelStatus.Downloading)
        {
            return Task.CompletedTask;
        }

        var cts = new CancellationTokenSource();
        _downloadTokens[modelId] = cts;

        var totalBytes = modelId.Contains("Phi") ? 3_800_000_000 : 120_000_000;
        var initialProgress = new ModelDownloadProgress(
            ModelId: modelId,
            Status: ModelStatus.Downloading,
            BytesReceived: 0,
            TotalBytes: totalBytes,
            Percentage: 0.0,
            DownloadSpeedEstimate: "Initializing..."
        );
        _activeDownloads[modelId] = initialProgress;

        // Run mock background download increments
        _ = Task.Run(async () =>
        {
            try
            {
                long bytesReceived = 0;
                var random = new Random();
                var step = totalBytes / 20; // 5% steps

                while (bytesReceived < totalBytes)
                {
                    await Task.Delay(200, cts.Token);
                    
                    bytesReceived = Math.Min(bytesReceived + step, totalBytes);
                    var pct = Math.Round((double)bytesReceived / totalBytes * 100, 1);
                    var speed = $"{random.Next(15, 35)} MB/s";

                    var current = new ModelDownloadProgress(
                        ModelId: modelId,
                        Status: bytesReceived >= totalBytes ? ModelStatus.Ready : ModelStatus.Downloading,
                        BytesReceived: bytesReceived,
                        TotalBytes: totalBytes,
                        Percentage: pct,
                        DownloadSpeedEstimate: speed
                    );
                    _activeDownloads[modelId] = current;
                }

                // Mock save physical file to disk so subsequent calls report Ready
                var settings = _settingsManager.GetSettings();
                var modelFolder = Path.Combine(settings.ModelStoragePath, modelId);
                if (!Directory.Exists(modelFolder))
                {
                    Directory.CreateDirectory(modelFolder);
                }
                var modelPath = Path.Combine(modelFolder, "model.onnx");
                if (!File.Exists(modelPath))
                {
                    await File.WriteAllTextAsync(modelPath, "MOCK ONNX MODEL WEIGHTS", cts.Token);
                }

                _logger.LogInformation("Model {ModelId} successfully downloaded and verified.", modelId);
                _activeDownloads.TryRemove(modelId, out _);
                _downloadTokens.TryRemove(modelId, out _);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Download for {ModelId} was canceled.", modelId);
                var current = new ModelDownloadProgress(
                    ModelId: modelId,
                    Status: ModelStatus.Failed,
                    BytesReceived: 0,
                    TotalBytes: totalBytes,
                    Percentage: 0.0,
                    DownloadSpeedEstimate: "Canceled"
                );
                _activeDownloads[modelId] = current;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Download for {ModelId} failed.", modelId);
                var current = new ModelDownloadProgress(
                    ModelId: modelId,
                    Status: ModelStatus.Failed,
                    BytesReceived: 0,
                    TotalBytes: totalBytes,
                    Percentage: 0.0,
                    DownloadSpeedEstimate: "Failed: " + ex.Message
                );
                _activeDownloads[modelId] = current;
            }
        });

        return Task.CompletedTask;
    }

    public Task CancelDownloadAsync(string modelId)
    {
        if (_downloadTokens.TryGetValue(modelId, out var cts))
        {
            cts.Cancel();
            _downloadTokens.TryRemove(modelId, out _);
        }
        return Task.CompletedTask;
    }
}
