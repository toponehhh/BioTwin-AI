using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OnnxSessionOptions = Microsoft.ML.OnnxRuntime.SessionOptions;

namespace BioTwin_AI.Services
{
    public sealed class BgeM3OnnxEmbeddingModel : ILocalEmbeddingModel, IDisposable
    {
        private const int DefaultMaxTokens = 8192;
        private const string DefaultModelDirectory = "LLM";
        private const string DefaultModelFileName = "bge_m3_model.onnx";
        private const string DefaultTokenizerFileName = "bge_m3_tokenizer.onnx";

        private readonly InferenceSession _tokenizerSession;
        private readonly InferenceSession _modelSession;
        private readonly int _maxTokens;
        private bool _disposed;

        public BgeM3OnnxEmbeddingModel(
            IHostEnvironment environment,
            IConfiguration configuration,
            ILogger<BgeM3OnnxEmbeddingModel> logger)
        {
            _maxTokens = Math.Max(1, configuration.GetValue("Embedding:MaxTokens", DefaultMaxTokens));

            var modelDirectory = ResolveModelDirectory(environment, configuration);
            var tokenizerPath = ResolveModelFile(
                modelDirectory,
                configuration["Embedding:TokenizerPath"],
                DefaultTokenizerFileName);
            var modelPath = ResolveModelFile(
                modelDirectory,
                configuration["Embedding:ModelPath"],
                DefaultModelFileName);

            using var sessionOptions = CreateSessionOptions();
            _tokenizerSession = new InferenceSession(tokenizerPath, sessionOptions);

            using var modelSessionOptions = CreateSessionOptions();
            _modelSession = new InferenceSession(modelPath, modelSessionOptions);

            logger.LogInformation(
                "Loaded local BGE-M3 embedding model from {ModelPath} with tokenizer {TokenizerPath}",
                modelPath,
                tokenizerPath);
        }

        public float[] GenerateEmbedding(string text)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var tokenIds = Tokenize(text ?? string.Empty);
            if (tokenIds.Length > _maxTokens)
            {
                tokenIds = tokenIds[.._maxTokens];
            }

            if (tokenIds.Length == 0)
            {
                throw new InvalidOperationException("BGE-M3 tokenizer returned no tokens.");
            }

            return RunEmbeddingModel(tokenIds);
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
                ?? results.FirstOrDefault();

            if (tokens is null)
            {
                throw new InvalidOperationException("BGE-M3 tokenizer returned no outputs.");
            }

            return tokens.AsTensor<int>().ToArray();
        }

        private float[] RunEmbeddingModel(int[] tokenIds)
        {
            var ids = tokenIds.Select(token => (long)token).ToArray();
            var mask = Enumerable.Repeat(1L, ids.Length).ToArray();

            var inputIds = new DenseTensor<long>(ids, new[] { 1, ids.Length });
            var attentionMask = new DenseTensor<long>(mask, new[] { 1, mask.Length });

            using var results = _modelSession.Run(new[]
            {
                NamedOnnxValue.CreateFromTensor("input_ids", inputIds),
                NamedOnnxValue.CreateFromTensor("attention_mask", attentionMask)
            });

            var dense = results.FirstOrDefault(result => string.Equals(result.Name, "dense_embeddings", StringComparison.OrdinalIgnoreCase))
                ?? results.FirstOrDefault();

            if (dense is null)
            {
                throw new InvalidOperationException("BGE-M3 model returned no embedding outputs.");
            }

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
                return ResolvePath(environment.ContentRootPath, configuredDirectory);
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
            var resolved = string.IsNullOrWhiteSpace(configuredPath)
                ? Path.Combine(modelDirectory, defaultFileName)
                : ResolvePath(modelDirectory, configuredPath);

            if (!File.Exists(resolved))
            {
                throw new FileNotFoundException($"Local BGE-M3 model file was not found: {resolved}", resolved);
            }

            return resolved;
        }

        private static string ResolvePath(string basePath, string path)
        {
            return Path.IsPathRooted(path)
                ? path
                : Path.GetFullPath(Path.Combine(basePath, path));
        }
    }
}
