using System.Numerics;

namespace RaidStream;

public static class Extensions
{
    public static void XorBuffers(byte[] target, byte[] source, int length)
    {
        int i = 0;
        int simdLength = Vector<byte>.Count;
        int simdEnd = length - (length % simdLength);

        // SIMD loop
        for (; i < simdEnd; i += simdLength)
        {
            var vTarget = new Vector<byte>(target, i);
            var vSource = new Vector<byte>(source, i);
            (vTarget ^ vSource).CopyTo(target, i);
        }
        // Scalar fallback for remaining bytes
        for (; i < length; i++)
        {
            target[i] ^= source[i];
        }
    }
}

