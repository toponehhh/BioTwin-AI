using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;
using System.Text.Json;
using OnnxSessionOptions = Microsoft.ML.OnnxRuntime.SessionOptions;

namespace BioTwin_AI.Services;

public sealed class LocalBgeRerankService : IRerankService, IDisposable
{
    private const int DefaultMaxTokens = 8192;
    private static readonly string DefaultModelDirectory = Path.Combine("LLM", "bge_rerank_v2");
    private const string DefaultModelFileName = "model.onnx";
    private const string DefaultTokenizerFileName = "tokenizer.json";
    private const int BosTokenId = 0;
    private const int PadTokenId = 1;
    private const int EosTokenId = 2;
    private const int UnknownTokenId = 3;

    private readonly InferenceSession _modelSession;
    private readonly SentencePieceTokenizer _tokenizer;
    private readonly int _maxTokens;
    private bool _disposed;

    public LocalBgeRerankService(
        IHostEnvironment environment,
        IConfiguration configuration,
        ILogger<LocalBgeRerankService> logger)
    {
        _maxTokens = Math.Max(8, configuration.GetValue("Rerank:MaxTokens", DefaultMaxTokens));

        var modelDirectory = ResolveModelDirectory(environment, configuration);
        var modelPath = ResolveModelFile(modelDirectory, configuration["Rerank:ModelPath"], DefaultModelFileName);
        var tokenizerPath = ResolveModelFile(modelDirectory, configuration["Rerank:TokenizerPath"], DefaultTokenizerFileName);

        _tokenizer = CreateTokenizer(tokenizerPath);

        using var sessionOptions = CreateSessionOptions();
        _modelSession = new InferenceSession(modelPath, sessionOptions);

        logger.LogInformation(
            "Loaded local BGE rerank model from {ModelPath} with tokenizer {TokenizerPath}",
            modelPath,
            tokenizerPath);
    }

    public Task<IReadOnlyList<RerankResult>> RerankAsync(
        string query,
        IReadOnlyList<string> documents,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(query) || documents.Count == 0 || limit <= 0)
        {
            return Task.FromResult<IReadOnlyList<RerankResult>>(Array.Empty<RerankResult>());
        }

        var results = new List<RerankResult>(documents.Count);
        for (var index = 0; index < documents.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(documents[index]))
            {
                continue;
            }

            var tokenIds = BuildPairTokenIds(query, documents[index]);
            var score = ScorePair(tokenIds);
            results.Add(new RerankResult(index, score));
        }

        var ordered = results
            .OrderByDescending(result => result.Score)
            .Take(limit)
            .ToList();

        return Task.FromResult<IReadOnlyList<RerankResult>>(ordered);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _modelSession.Dispose();
        _disposed = true;
    }

    private int[] BuildPairTokenIds(string query, string document)
    {
        var queryIds = EncodeWithoutSpecialTokens(query);
        var documentIds = EncodeWithoutSpecialTokens(document);
        var reservedTokenCount = 4;
        var maxContentTokens = Math.Max(1, _maxTokens - reservedTokenCount);

        if (queryIds.Count + documentIds.Count > maxContentTokens)
        {
            var queryLimit = Math.Min(queryIds.Count, Math.Max(1, maxContentTokens / 3));
            var documentLimit = Math.Max(1, maxContentTokens - queryLimit);
            queryIds = queryIds.Take(queryLimit).ToList();
            documentIds = documentIds.Take(documentLimit).ToList();
        }

        return new[]
            {
                BosTokenId
            }
            .Concat(queryIds)
            .Concat(new[] { EosTokenId, EosTokenId })
            .Concat(documentIds)
            .Concat(new[] { EosTokenId })
            .ToArray();
    }

    private IReadOnlyList<int> EncodeWithoutSpecialTokens(string text)
    {
        return _tokenizer.EncodeToIds(
            text ?? string.Empty,
            addBeginningOfSentence: false,
            addEndOfSentence: false,
            considerPreTokenization: true,
            considerNormalization: true);
    }

    private double ScorePair(int[] tokenIds)
    {
        var ids = tokenIds.Select(id => (long)id).ToArray();
        var inputIds = new DenseTensor<long>(ids, new[] { 1, ids.Length });
        var attentionMask = new DenseTensor<long>(Enumerable.Repeat(1L, ids.Length).ToArray(), new[] { 1, ids.Length });
        var tokenTypeIds = new DenseTensor<long>(new long[ids.Length], new[] { 1, ids.Length });

        var inputs = new List<NamedOnnxValue>();
        foreach (var inputName in _modelSession.InputMetadata.Keys)
        {
            if (string.Equals(inputName, "input_ids", StringComparison.OrdinalIgnoreCase))
            {
                inputs.Add(NamedOnnxValue.CreateFromTensor(inputName, inputIds));
            }
            else if (string.Equals(inputName, "attention_mask", StringComparison.OrdinalIgnoreCase))
            {
                inputs.Add(NamedOnnxValue.CreateFromTensor(inputName, attentionMask));
            }
            else if (string.Equals(inputName, "token_type_ids", StringComparison.OrdinalIgnoreCase))
            {
                inputs.Add(NamedOnnxValue.CreateFromTensor(inputName, tokenTypeIds));
            }
        }

        using var outputs = _modelSession.Run(inputs);
        var logits = outputs.FirstOrDefault()?.AsTensor<float>().ToArray();
        if (logits is null || logits.Length == 0)
        {
            return 0;
        }

        return Sigmoid(logits[0]);
    }

    private static SentencePieceTokenizer CreateTokenizer(string tokenizerPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(tokenizerPath));
        var root = document.RootElement;
        using var modelStream = new MemoryStream(BuildSentencePieceModel(root));

        var specialTokens = new Dictionary<string, int>
        {
            ["<s>"] = BosTokenId,
            ["<pad>"] = PadTokenId,
            ["</s>"] = EosTokenId,
            ["<unk>"] = UnknownTokenId,
            ["<mask>"] = 250001
        };

        return SentencePieceTokenizer.Create(
            modelStream,
            addBeginningOfSentence: false,
            addEndOfSentence: false,
            specialTokens);
    }

    private static byte[] BuildSentencePieceModel(JsonElement root)
    {
        var model = new MemoryStream();
        var vocab = root.GetProperty("model").GetProperty("vocab");

        foreach (var pieceElement in vocab.EnumerateArray())
        {
            var piece = pieceElement[0].GetString() ?? string.Empty;
            using var pieceMessage = new MemoryStream();
            WriteString(pieceMessage, 1, piece);
            WriteFloat(pieceMessage, 2, pieceElement[1].GetSingle());
            WriteVarint(pieceMessage, 3, GetSentencePieceType(piece));
            WriteMessage(model, 1, pieceMessage.ToArray());
        }

        WriteMessage(model, 2, BuildTrainerSpec(vocab.GetArrayLength()));
        WriteMessage(model, 3, BuildNormalizerSpec(root));
        return model.ToArray();
    }

    private static byte[] BuildTrainerSpec(int vocabularySize)
    {
        using var trainer = new MemoryStream();
        WriteVarint(trainer, 3, 1); // UNIGRAM
        WriteVarint(trainer, 4, vocabularySize);
        WriteFloat(trainer, 10, 0.9995f);
        WriteVarint(trainer, 22, 1);
        WriteVarint(trainer, 32, 1);
        WriteVarint(trainer, 40, UnknownTokenId);
        WriteVarint(trainer, 41, BosTokenId);
        WriteVarint(trainer, 42, EosTokenId);
        WriteVarint(trainer, 43, PadTokenId);
        WriteString(trainer, 45, "<unk>");
        WriteString(trainer, 46, "<s>");
        WriteString(trainer, 47, "</s>");
        WriteString(trainer, 48, "<pad>");
        return trainer.ToArray();
    }

    private static byte[] BuildNormalizerSpec(JsonElement root)
    {
        using var normalizer = new MemoryStream();
        WriteString(normalizer, 1, "nmt_nfkc");

        if (root.TryGetProperty("normalizer", out var normalizerElement) &&
            normalizerElement.TryGetProperty("normalizers", out var normalizers))
        {
            foreach (var item in normalizers.EnumerateArray())
            {
                if (item.TryGetProperty("precompiled_charsmap", out var charsMap) &&
                    charsMap.GetString() is { Length: > 0 } encodedCharsMap)
                {
                    WriteBytes(normalizer, 2, Convert.FromBase64String(encodedCharsMap));
                    break;
                }
            }
        }

        WriteVarint(normalizer, 3, 1);
        WriteVarint(normalizer, 4, 1);
        WriteVarint(normalizer, 5, 1);
        return normalizer.ToArray();
    }

    private static int GetSentencePieceType(string piece)
    {
        return piece switch
        {
            "<unk>" => 2,
            "<s>" or "</s>" or "<pad>" => 3,
            "<mask>" => 4,
            _ => 1
        };
    }

    private static void WriteMessage(Stream stream, int fieldNumber, byte[] message)
    {
        WriteKey(stream, fieldNumber, 2);
        WriteRawVarint(stream, (ulong)message.Length);
        stream.Write(message);
    }

    private static void WriteString(Stream stream, int fieldNumber, string value)
    {
        WriteBytes(stream, fieldNumber, System.Text.Encoding.UTF8.GetBytes(value));
    }

    private static void WriteBytes(Stream stream, int fieldNumber, byte[] value)
    {
        WriteKey(stream, fieldNumber, 2);
        WriteRawVarint(stream, (ulong)value.Length);
        stream.Write(value);
    }

    private static void WriteFloat(Stream stream, int fieldNumber, float value)
    {
        WriteKey(stream, fieldNumber, 5);
        Span<byte> buffer = stackalloc byte[4];
        BitConverter.TryWriteBytes(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteVarint(Stream stream, int fieldNumber, int value)
    {
        WriteKey(stream, fieldNumber, 0);
        WriteRawVarint(stream, (ulong)value);
    }

    private static void WriteKey(Stream stream, int fieldNumber, int wireType)
    {
        WriteRawVarint(stream, (ulong)((fieldNumber << 3) | wireType));
    }

    private static void WriteRawVarint(Stream stream, ulong value)
    {
        while (value > 0x7F)
        {
            stream.WriteByte((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }

        stream.WriteByte((byte)value);
    }

    private static OnnxSessionOptions CreateSessionOptions()
    {
        return new OnnxSessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            InterOpNumThreads = 1,
            IntraOpNumThreads = 1
        };
    }

    private static double Sigmoid(float value)
    {
        return 1d / (1d + Math.Exp(-value));
    }

    private static string ResolveModelDirectory(IHostEnvironment environment, IConfiguration configuration)
    {
        var configuredDirectory = configuration["Rerank:ModelDirectory"];
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
            throw new FileNotFoundException($"Local BGE rerank model file was not found: {resolved}", resolved);
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
