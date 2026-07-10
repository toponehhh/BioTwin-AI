namespace BioTwin_AI.AspNetCoreApi.Application.Embeddings;

public static class EmbeddingVectorValidator
{
    public static float[] ValidateAndNormalize(IReadOnlyList<float> vector, int expectedDimensions)
    {
        if (vector.Count != expectedDimensions)
        {
            throw new EmbeddingResponseException(
                $"Embedding vector dimensions were invalid. Expected {expectedDimensions}, received {vector.Count}.");
        }

        var result = new float[vector.Count];
        var sumSquares = 0d;
        for (var index = 0; index < vector.Count; index++)
        {
            var value = vector[index];
            if (!float.IsFinite(value))
            {
                throw new EmbeddingResponseException("Embedding vector contained a non-finite value.");
            }

            result[index] = value;
            var doubleValue = (double)value;
            sumSquares += doubleValue * doubleValue;
        }

        if (sumSquares <= 0d || !double.IsFinite(sumSquares))
        {
            throw new EmbeddingResponseException("Embedding vector was empty or had zero magnitude.");
        }

        var norm = Math.Sqrt(sumSquares);
        for (var index = 0; index < result.Length; index++)
        {
            result[index] = (float)(result[index] / norm);
        }

        return result;
    }
}
