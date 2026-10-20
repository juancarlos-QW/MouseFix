using System.Buffers.Binary;

namespace MouseFix.Core;

/// <summary>
/// Builds the SmoothMouseXCurve / SmoothMouseYCurve registry values.
/// Each curve is 5 points of 8 bytes; the low 4 bytes are a 16.16 fixed-point number.
/// </summary>
public static class SmoothMouseCurve
{
    public const int PointCount = 5;
    public const int ByteLength = PointCount * 8;

    // Y step is 3.5 in 16.16 fixed point, regardless of display scaling.
    private const int LinearYStep = 0x38000;

    public static readonly byte[] WindowsDefaultX = FromPoints(0, 0x6E15, 0x14000, 0x3DC29, 0x280000);
    public static readonly byte[] WindowsDefaultY = FromPoints(0, 0x111FD, 0x42400, 0x12FC00, 0x1BBC000);

    /// <summary>
    /// Linear (1:1) X curve for the given display scaling, so that even if a program
    /// forces "Enhance pointer precision" on, movement stays unaccelerated.
    /// 100% -> 0xCCCC0, 125% -> 0x100000, 150% -> 0x133330 (same values as the well-known MarkC fix).
    /// </summary>
    public static byte[] LinearX(int scalePercent)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(scalePercent, 100);
        // 12.8 * scale in 16.16 fixed point, truncated to a multiple of 0x10. Integer math avoids float drift.
        int step = (int)(4194304L * scalePercent / 8000) * 16;
        return FromPoints(0, step, step * 2, step * 3, step * 4);
    }

    public static byte[] LinearY() => FromPoints(0, LinearYStep, LinearYStep * 2, LinearYStep * 3, LinearYStep * 4);

    public static byte[] FromPoints(params int[] points)
    {
        if (points.Length != PointCount)
            throw new ArgumentException($"A curve needs exactly {PointCount} points.", nameof(points));

        var bytes = new byte[ByteLength];
        for (int i = 0; i < PointCount; i++)
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i * 8), points[i]);
        return bytes;
    }

    public static int[] ToPoints(byte[] curve)
    {
        if (curve.Length != ByteLength)
            throw new ArgumentException($"A curve must be {ByteLength} bytes.", nameof(curve));

        var points = new int[PointCount];
        for (int i = 0; i < PointCount; i++)
            points[i] = BinaryPrimitives.ReadInt32LittleEndian(curve.AsSpan(i * 8));
        return points;
    }
}
