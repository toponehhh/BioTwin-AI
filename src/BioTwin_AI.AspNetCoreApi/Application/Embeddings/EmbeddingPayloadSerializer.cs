using System.Globalization;

namespace BioTwin_AI.AspNetCoreApi.Application.Embeddings;

public static class EmbeddingPayloadSerializer
{
    public static string Serialize(IReadOnlyList<float> vector)
    {
        return string.Join(",", vector.Select(value => value.ToString("R", CultureInfo.InvariantCulture)));
    }

    public static float[] Deserialize(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return [];
        }

        return payload
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0f)
            .ToArray();
    }
}
