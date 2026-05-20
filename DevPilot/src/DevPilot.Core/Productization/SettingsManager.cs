using System;
using System.IO;
using System.Text.Json;
using DevPilot.Contracts;

namespace DevPilot.Core.Productization;

public sealed class SettingsManager : ISettingsManager
{
    private readonly string _settingsFilePath;
    private readonly object _lock = new();

    public SettingsManager()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "DevPilot");
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        _settingsFilePath = Path.Combine(dir, "devpilot-settings.json");
    }

    // Secondary constructor allowing custom file path for testing
    public SettingsManager(string settingsFilePath)
    {
        _settingsFilePath = settingsFilePath;
        var dir = Path.GetDirectoryName(settingsFilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    public ProductSettings GetSettings()
    {
        lock (_lock)
        {
            if (!File.Exists(_settingsFilePath))
            {
                var defaults = new ProductSettings(
                    ModelStoragePath: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DevPilot", "models"),
                    ActiveLlmModel: "Phi-3-Mini-Instruct-ONNX",
                    ActiveEmbeddingModel: "All-MiniLM-L6-v2-ONNX",
                    HardwareProviderPreference: "DirectML",
                    LogLevelThreshold: "Information"
                );
                SaveSettings(defaults);
                return defaults;
            }

            try
            {
                var json = File.ReadAllText(_settingsFilePath);
                var settings = JsonSerializer.Deserialize<ProductSettings>(json);
                return settings ?? new ProductSettings(
                    ModelStoragePath: "",
                    ActiveLlmModel: "",
                    ActiveEmbeddingModel: "",
                    HardwareProviderPreference: "",
                    LogLevelThreshold: ""
                );
            }
            catch
            {
                // Fallback on corrupt json
                return new ProductSettings(
                    ModelStoragePath: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DevPilot", "models"),
                    ActiveLlmModel: "Phi-3-Mini-Instruct-ONNX",
                    ActiveEmbeddingModel: "All-MiniLM-L6-v2-ONNX",
                    HardwareProviderPreference: "DirectML",
                    LogLevelThreshold: "Information"
                );
            }
        }
    }

    public void SaveSettings(ProductSettings settings)
    {
        lock (_lock)
        {
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsFilePath, json);
        }
    }
}
