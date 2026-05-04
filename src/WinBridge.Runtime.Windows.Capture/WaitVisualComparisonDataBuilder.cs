// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using Windows.Graphics.Imaging;
using Windows.Foundation;
using WinRT;

namespace WinBridge.Runtime.Windows.Capture;

internal static class WaitVisualComparisonDataBuilder
{
    public static WaitVisualComparisonData CreateFromBgra32Pixels(
        int pixelWidth,
        int pixelHeight,
        int rowStride,
        ReadOnlySpan<byte> pixelBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rowStride);

        long[] sums = new long[WaitVisualGridPolicy.TotalCellCount];
        int[] counts = new int[WaitVisualGridPolicy.TotalCellCount];

        for (int y = 0; y < pixelHeight; y++)
        {
            if ((y & 31) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            int rowOffset = y * rowStride;
            int cellY = y * WaitVisualGridPolicy.GridHeight / pixelHeight;
            for (int x = 0; x < pixelWidth; x++)
            {
                int pixelOffset = rowOffset + (x * 4);
                byte blue = pixelBytes[pixelOffset];
                byte green = pixelBytes[pixelOffset + 1];
                byte red = pixelBytes[pixelOffset + 2];
                int luma = ((red * 77) + (green * 150) + (blue * 29) + 128) >> 8;
                int cellX = x * WaitVisualGridPolicy.GridWidth / pixelWidth;
                int cellIndex = (cellY * WaitVisualGridPolicy.GridWidth) + cellX;
                sums[cellIndex] += luma;
                counts[cellIndex]++;
            }
        }

        return BuildGrid(sums, counts);
    }

    public static unsafe WaitVisualComparisonData CreateFromSoftwareBitmap(
        SoftwareBitmap softwareBitmap,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(softwareBitmap);

        using (BitmapBuffer buffer = softwareBitmap.LockBuffer(BitmapBufferAccessMode.Read))
        {
            BitmapPlaneDescription plane = buffer.GetPlaneDescription(0);
            using IMemoryBufferReference reference = buffer.CreateReference();
            IMemoryBufferByteAccess byteAccess;
            try
            {
                byteAccess = reference.As<IMemoryBufferByteAccess>();
            }
            catch (InvalidCastException exception)
            {
                throw new CaptureOperationException("Runtime не смог получить direct pixel buffer access для visual wait sample.", exception);
            }

            byteAccess.GetBuffer(out byte* bytes, out uint capacity);
            if (bytes is null)
            {
                throw new CaptureOperationException("Runtime не смог прочитать pixel buffer visual wait sample.");
            }

            int requiredBytes = plane.StartIndex + (plane.Stride * plane.Height);
            if (requiredBytes < 0 || requiredBytes > capacity)
            {
                throw new CaptureOperationException("Runtime получил неконсистентный размер pixel buffer visual wait sample.");
            }

            ReadOnlySpan<byte> pixelBytes = new(bytes + plane.StartIndex, plane.Stride * plane.Height);
            return CreateFromBgra32Pixels(
                softwareBitmap.PixelWidth,
                softwareBitmap.PixelHeight,
                plane.Stride,
                pixelBytes,
                cancellationToken);
        }
    }

    private static WaitVisualComparisonData BuildGrid(long[] sums, int[] counts)
    {
        byte[] grid = new byte[WaitVisualGridPolicy.TotalCellCount];
        int populatedCellCount = 0;
        for (int index = 0; index < WaitVisualGridPolicy.TotalCellCount; index++)
        {
            if (counts[index] == 0)
            {
                grid[index] = 0;
                continue;
            }

            populatedCellCount++;
            grid[index] = (byte)(sums[index] / counts[index]);
        }

        return new WaitVisualComparisonData(grid, populatedCellCount);
    }

    private static class WaitVisualGridPolicy
    {
        public const int GridWidth = 16;
        public const int GridHeight = 16;
        public const int TotalCellCount = GridWidth * GridHeight;
    }
}
