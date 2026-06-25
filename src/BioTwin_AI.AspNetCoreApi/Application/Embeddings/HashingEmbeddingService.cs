using System.Security.Cryptography;
using System.Text;

namespace BioTwin_AI.AspNetCoreApi.Application.Embeddings;

public sealed class HashingEmbeddingService(IConfiguration configuration) : IEmbeddingService
{
    private readonly int _vectorSize = Math.Max(16, configuration.GetValue("Rag:EmbeddingSize", 1024));

    public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var vector = new float[_vectorSize];
        foreach (var token in Tokenize(text))
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            var index = BitConverter.ToUInt32(hash, 0) % (uint)_vectorSize;
            var sign = (hash[4] & 1) == 0 ? 1f : -1f;
            vector[index] += sign;
        }

        Normalize(vector);
        return Task.FromResult(vector);
    }

    private static IEnumerable<string> Tokenize(string? text)
    {
        var normalized = (text ?? string.Empty).ToLowerInvariant();
        var builder = new StringBuilder();

        foreach (var ch in normalized)
        {
            if (char.IsLetterOrDigit(ch) || ch is '#' or '+' or '.')
            {
                builder.Append(ch);
                continue;
            }

            if (builder.Length > 0)
            {
                yield return builder.ToString();
                builder.Clear();
            }
        }

        if (builder.Length > 0)
        {
            yield return builder.ToString();
        }
    }

    private static void Normalize(float[] vector)
    {
        var sum = 0d;
        foreach (var value in vector)
        {
            sum += value * value;
        }

        if (sum <= 0)
        {
            return;
        }

        var norm = Math.Sqrt(sum);
        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] = (float)(vector[i] / norm);
        }
    }
}
