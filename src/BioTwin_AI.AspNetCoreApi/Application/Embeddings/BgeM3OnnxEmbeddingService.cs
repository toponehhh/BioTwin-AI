using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OnnxSessionOptions = Microsoft.ML.OnnxRuntime.SessionOptions;

namespace BioTwin_AI.AspNetCoreApi.Application.Embeddings;

public sealed class BgeM3OnnxEmbeddingService : IEmbeddingService, IDisposable
{
    private const int DefaultMaxTokens = 8192;
    private static readonly string DefaultModelDirectory = Path.Combine("LLM", "bge_m3");
    private const string DefaultModelFileName = "bge_m3_model.onnx";
    private const string DefaultTokenizerFileName = "bge_m3_tokenizer.onnx";

    private readonly InferenceSession _tokenizerSession;
    private readonly InferenceSession _modelSession;
    private readonly int _maxTokens;
    private bool _disposed;

    public BgeM3OnnxEmbeddingService(IHostEnvironment environment, IConfiguration configuration, ILogger<BgeM3OnnxEmbeddingService> logger)
    {
        _maxTokens = Math.Max(1, configuration.GetValue("Embedding:MaxTokens", DefaultMaxTokens));

        var modelDirectory = ResolveModelDirectory(environment, configuration);
        var tokenizerPath = ResolveModelFile(modelDirectory, configuration["Embedding:TokenizerPath"], DefaultTokenizerFileName);
        var modelPath = ResolveModelFile(modelDirectory, configuration["Embedding:ModelPath"], DefaultModelFileName);

        using var tokenizerOptions = CreateSessionOptions();
        _tokenizerSession = new InferenceSession(tokenizerPath, tokenizerOptions);

        using var modelOptions = CreateSessionOptions();
        _modelSession = new InferenceSession(modelPath, modelOptions);

        logger.LogInformation("Loaded BGE-M3 ONNX embedding model from {ModelPath}", modelPath);
    }

    public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        var tokenIds = Tokenize(text ?? string.Empty);
        if (tokenIds.Length > _maxTokens)
        {
            tokenIds = tokenIds[.._maxTokens];
        }

        if (tokenIds.Length == 0)
        {
            return Task.FromResult(Array.Empty<float>());
        }

        return Task.FromResult(RunEmbeddingModel(tokenIds));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _tokenizerSession.Dispose();
        _modelSession.Dispose();
        _disposed = true;
    }

    public static bool CanLoad(IHostEnvironment environment, IConfiguration configuration)
    {
        var modelDirectory = ResolveModelDirectory(environment, configuration);
        var tokenizerPath = ResolvePath(modelDirectory, configuration["Embedding:TokenizerPath"] ?? DefaultTokenizerFileName);
        var modelPath = ResolvePath(modelDirectory, configuration["Embedding:ModelPath"] ?? DefaultModelFileName);
        return File.Exists(tokenizerPath) && File.Exists(modelPath);
    }

    private static OnnxSessionOptions CreateSessionOptions()
    {
        var options = new OnnxSessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
        };
        options.RegisterOrtExtensions();
        return options;
    }

    private int[] Tokenize(string text)
    {
        var inputName = _tokenizerSession.InputMetadata.Keys.Single();
        var input = new DenseTensor<string>(new[] { text }, new[] { 1 });

        using var results = _tokenizerSession.Run(new[]
        {
            NamedOnnxValue.CreateFromTensor(inputName, input)
        });
        var tokens = results.FirstOrDefault(result => string.Equals(result.Name, "tokens", StringComparison.OrdinalIgnoreCase))
            ?? results.FirstOrDefault()
            ?? throw new InvalidOperationException("BGE-M3 tokenizer returned no outputs.");

        return tokens.AsTensor<int>().ToArray();
    }

    private float[] RunEmbeddingModel(int[] tokenIds)
    {
        var ids = tokenIds.Select(token => (long)token).ToArray();
        var mask = Enumerable.Repeat(1L, ids.Length).ToArray();
        var inputIds = new DenseTensor<long>(ids, new[] { 1, ids.Length });
        var attentionMask = new DenseTensor<long>(mask, new[] { 1, mask.Length });

        using var results = _modelSession.Run(
        new[]
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputIds),
            NamedOnnxValue.CreateFromTensor("attention_mask", attentionMask)
        });

        var dense = results.FirstOrDefault(result => string.Equals(result.Name, "dense_embeddings", StringComparison.OrdinalIgnoreCase))
            ?? results.FirstOrDefault()
            ?? throw new InvalidOperationException("BGE-M3 model returned no embedding outputs.");

        return Normalize(dense.AsTensor<float>().ToArray());
    }

    private static float[] Normalize(float[] vector)
    {
        var sumSquares = 0.0;
        foreach (var value in vector)
        {
            sumSquares += value * value;
        }

        if (sumSquares <= 0)
        {
            return vector;
        }

        var norm = Math.Sqrt(sumSquares);
        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] = (float)(vector[i] / norm);
        }

        return vector;
    }

    private static string ResolveModelDirectory(IHostEnvironment environment, IConfiguration configuration)
    {
        var configuredDirectory = configuration["Embedding:ModelDirectory"];
        if (!string.IsNullOrWhiteSpace(configuredDirectory))
        {
            var configuredPath = ResolvePath(environment.ContentRootPath, configuredDirectory);
            if (Directory.Exists(configuredPath))
            {
                return configuredPath;
            }

            var publishRelativePath = GetPathFromLlmSegment(configuredDirectory);
            if (publishRelativePath is not null)
            {
                var publishPath = ResolvePath(AppContext.BaseDirectory, publishRelativePath);
                if (Directory.Exists(publishPath))
                {
                    return publishPath;
                }
            }

            return configuredPath;
        }

        var solutionRoot = FindSolutionRoot(environment.ContentRootPath);
        if (solutionRoot is not null)
        {
            var sharedDirectory = Path.Combine(solutionRoot, DefaultModelDirectory);
            if (Directory.Exists(sharedDirectory))
            {
                return sharedDirectory;
            }
        }

        var contentRootDirectory = Path.Combine(environment.ContentRootPath, DefaultModelDirectory);
        if (Directory.Exists(contentRootDirectory))
        {
            return contentRootDirectory;
        }

        return Path.Combine(AppContext.BaseDirectory, DefaultModelDirectory);
    }

    private static string ResolveModelFile(string modelDirectory, string? configuredPath, string defaultFileName)
    {
        var resolved = ResolvePath(modelDirectory, configuredPath ?? defaultFileName);
        if (!File.Exists(resolved))
        {
            throw new FileNotFoundException($"Local BGE-M3 model file was not found: {resolved}", resolved);
        }

        return resolved;
    }

    private static string ResolvePath(string basePath, string path)
    {
        return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(basePath, path));
    }

    private static string? FindSolutionRoot(string startPath)
    {
        var current = new DirectoryInfo(startPath);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "BioTwin_AI.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    private static string? GetPathFromLlmSegment(string path)
    {
        var segments = path.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);
        var llmIndex = Array.FindIndex(segments, segment => string.Equals(segment, "LLM", StringComparison.OrdinalIgnoreCase));
        return llmIndex < 0 ? null : Path.Combine(segments[llmIndex..]);
    }
}
