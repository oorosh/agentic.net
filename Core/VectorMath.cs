namespace Agentic.Core;

internal static class VectorMath
{
    /// <summary>
    /// Computes cosine similarity between two vectors of equal length.
    /// Returns 0 if either vector is a zero vector.
    /// </summary>
    internal static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
        {
            throw new ArgumentException("Embedding dimensions must match.");
        }

        float dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        // Handle zero vectors
        if (normA == 0 || normB == 0)
        {
            return 0f;
        }

        float similarity = dot / (MathF.Sqrt(normA) * MathF.Sqrt(normB));

        // Handle NaN (shouldn't happen with above check, but just in case)
        if (float.IsNaN(similarity))
        {
            return 0f;
        }

        return similarity;
    }
}
