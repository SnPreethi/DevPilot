using DevPilot.Contracts;
using DevPilot.Core;
using Microsoft.Extensions.Options;

namespace DevPilot.AI.Registry;

public sealed class ModelRegistry : IModelManager
{
    private readonly LLMSettings _settings;

    public ModelRegistry(IOptions<LLMSettings> settings)
    {
        _settings = settings.Value;
    }

    public ModelDescriptor Resolve(ExecutionProviderKind provider)
    {
        var basePath = Path.GetDirectoryName(_settings.ModelPath) ?? string.Empty;
        var modelName = Path.GetFileName(_settings.ModelPath);
        var tokenizerName = Path.GetFileName(_settings.TokenizerModelPath);

        return provider switch
        {
            ExecutionProviderKind.Cuda => LoadCudaModel(basePath, modelName, tokenizerName),
            ExecutionProviderKind.DirectML => LoadDirectMlModel(basePath, modelName, tokenizerName),
            _ => LoadCpuModel(basePath, modelName, tokenizerName)
        };
    }

    private ModelDescriptor LoadCudaModel(string basePath, string modelName, string tokenizerName)
    {
        var cudaPath = Path.Combine(basePath, "cuda", modelName);
        if (File.Exists(cudaPath))
        {
            return new ModelDescriptor
            {
                Name = "Phi-3 CUDA",
                ModelPath = cudaPath,
                TokenizerPath = ResolveTokenizerPath(basePath, "cuda", tokenizerName),
                Target = ModelExecutionTarget.Cuda,
                SupportsKvCache = true,
                SupportsStreaming = true,
                UsesFp16 = true
            };
        }
        return LoadDirectMlModel(basePath, modelName, tokenizerName);
    }

    private ModelDescriptor LoadDirectMlModel(string basePath, string modelName, string tokenizerName)
    {
        var dmlPath = Path.Combine(basePath, "directml", modelName);
        if (File.Exists(dmlPath))
        {
            return new ModelDescriptor
            {
                Name = "Phi-3 DirectML",
                ModelPath = dmlPath,
                TokenizerPath = ResolveTokenizerPath(basePath, "directml", tokenizerName),
                Target = ModelExecutionTarget.DirectML,
                SupportsKvCache = true,
                SupportsStreaming = true,
                UsesFp16 = true
            };
        }
        return LoadCpuModel(basePath, modelName, tokenizerName);
    }

    private ModelDescriptor LoadCpuModel(string basePath, string modelName, string tokenizerName)
    {
        var cpuPath = Path.Combine(basePath, "cpu", modelName);
        if (File.Exists(cpuPath))
        {
            return new ModelDescriptor
            {
                Name = "Phi-3 CPU",
                ModelPath = cpuPath,
                TokenizerPath = ResolveTokenizerPath(basePath, "cpu", tokenizerName),
                Target = ModelExecutionTarget.Cpu,
                SupportsKvCache = true,
                SupportsStreaming = true,
                UsesFp16 = false
            };
        }
        
        // Final fallback to original path if no provider folder is found
        return new ModelDescriptor
        {
            Name = "Phi-3 Default CPU",
            ModelPath = Path.Combine(basePath, modelName),
            TokenizerPath = Path.Combine(basePath, tokenizerName),
            Target = ModelExecutionTarget.Cpu,
            SupportsKvCache = true,
            SupportsStreaming = true,
            UsesFp16 = false
        };
    }

    private static string ResolveTokenizerPath(string basePath, string providerFolder, string tokenizerName)
    {
        var providerTokenizer = Path.Combine(basePath, providerFolder, tokenizerName);
        if (File.Exists(providerTokenizer))
        {
            return providerTokenizer;
        }
        // Fallback to shared tokenizer at base path
        return Path.Combine(basePath, tokenizerName);
    }
}