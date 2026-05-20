using System.Buffers.Binary;

namespace DevPilot.Storage;

internal static class VectorSerializer
{
    public static byte[] Serialize(IReadOnlyList<float> vector)
    {
        var bytes = new byte[vector.Count * sizeof(float)];
        for (var i = 0; i < vector.Count; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * sizeof(float), sizeof(float)), vector[i]);
        }

        return bytes;
    }

    public static float[] Deserialize(byte[] bytes)
    {
        var vector = new float[bytes.Length / sizeof(float)];
        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(i * sizeof(float), sizeof(float)));
        }

        return vector;
    }
}
