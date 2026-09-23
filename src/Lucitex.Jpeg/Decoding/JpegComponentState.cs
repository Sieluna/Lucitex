using Lucitex.Jpeg.Format;
using System.Buffers;

namespace Lucitex.Jpeg.Decoding;

internal sealed class JpegComponentState
{
    public required JpegComponent Component { get; init; }

    public required int SamplesPerLine { get; init; }

    public required int SamplesPerColumn { get; init; }

    public required int BlocksPerLine { get; init; }

    public required int BlocksPerColumn { get; init; }

    public required int BlocksPerLineForMcu { get; init; }

    public required int BlocksPerColumnForMcu { get; init; }

    public required short[] Coefficients { get; init; }

    public int DcPredictor;

    public int BlockOffset(int blockRow, int blockCol) => ((blockRow * BlocksPerLineForMcu) + blockCol) * 64;

    public static JpegComponentState[] BuildAll(JpegFrameHeader frame)
    {
        var hMax = frame.HMax;
        var vMax = frame.VMax;
        var mcusPerLine = CeilDiv(frame.Width, 8 * hMax);
        var mcusPerColumn = CeilDiv(frame.Height, 8 * vMax);

        var states = new JpegComponentState[frame.Components.Count];
        try {
            for (var i = 0; i < frame.Components.Count; i++) {
                var component = frame.Components[i];

                var blocksPerLine = CeilDiv(CeilDiv(frame.Width, 8) * component.HSampling, hMax);
                var blocksPerColumn = CeilDiv(CeilDiv(frame.Height, 8) * component.VSampling, vMax);
                var blocksPerLineForMcu = mcusPerLine * component.HSampling;
                var blocksPerColumnForMcu = mcusPerColumn * component.VSampling;

                var coefficientCount = checked(blocksPerLineForMcu * blocksPerColumnForMcu * 64);
                var coefficients = ArrayPool<short>.Shared.Rent(coefficientCount);
                coefficients.AsSpan(0, coefficientCount).Clear();
                states[i] = new JpegComponentState {
                    Component = component,
                    SamplesPerLine = CeilDiv(frame.Width * component.HSampling, hMax),
                    SamplesPerColumn = CeilDiv(frame.Height * component.VSampling, vMax),
                    BlocksPerLine = blocksPerLine,
                    BlocksPerColumn = blocksPerColumn,
                    BlocksPerLineForMcu = blocksPerLineForMcu,
                    BlocksPerColumnForMcu = blocksPerColumnForMcu,
                    Coefficients = coefficients,
                };
            }
            return states;
        }
        catch {
            foreach (var state in states) {
                if (state is not null) {
                    ArrayPool<short>.Shared.Return(state.Coefficients);
                }
            }
            throw;
        }
    }

    internal static int CeilDiv(int numerator, int denominator) => (numerator + denominator - 1) / denominator;
}
