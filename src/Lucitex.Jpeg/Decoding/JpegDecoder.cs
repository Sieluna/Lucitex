using Lucitex.Core.Execution;
using Lucitex.Jpeg.Format;
using System.Buffers;

namespace Lucitex.Jpeg.Decoding;

internal sealed class JpegDecoder
{
    private readonly JpegByteCursor _cursor;
    private readonly Dictionary<int, JpegQuantizationTable> _quantTables = new();
    private readonly JpegHuffmanDecodeTable?[] _huffmanTables = new JpegHuffmanDecodeTable[32];
    private readonly List<JpegAppSegment> _appSegments = new();
    private readonly List<string> _comments = new();
    private int _restartInterval;
    private JpegComponentState[] _components = [];

    public JpegDecoder(Stream stream) => _cursor = new JpegByteCursor(stream);

    public JpegFrameHeader Frame { get; private set; } = null!;

    public JpegQuantizationTable GetQuantTable(int id) => _quantTables.TryGetValue(id, out var table)
        ? table
        : throw new ImageFormatException("jpeg", "BadTableReference", $"JPEG scan references undefined quantization table {id}.");

    public IReadOnlyList<JpegAppSegment> AppSegments => _appSegments;

    public IReadOnlyList<string> Comments => _comments;

    public IReadOnlyList<JpegComponentState> Components => _components;

    public bool AdobeTransformIsRaw { get; private set; }

    public void ReleaseBuffers()
    {
        foreach (var component in _components) {
            ArrayPool<short>.Shared.Return(component.Coefficients);
        }
        _components = [];
        foreach (var table in _huffmanTables) {
            table?.Dispose();
        }
        Array.Clear(_huffmanTables);
    }

    public void ParseHeader()
    {
        var soi = _cursor.ReadMarker();
        if (soi != JpegMarkers.Soi) {
            throw new ImageFormatException("jpeg", "BadSignature", "JPEG stream does not start with an SOI marker.");
        }

        while (true) {
            var marker = _cursor.ReadMarker();
            switch (marker) {
                case JpegMarkers.Sof0 or JpegMarkers.Sof1:
                    ReadFrameHeader(progressive: false);
                    return;
                case JpegMarkers.Sof2:
                    ReadFrameHeader(progressive: true);
                    return;
                case JpegMarkers.Eoi:
                    throw new ImageFormatException("jpeg", "MissingFrameHeader", "JPEG stream reached EOI before a frame header was found.");
                default:
                    ReadMetadataSegment(marker);
                    break;
            }
        }
    }

    public void DecodeScans()
    {
        _components = JpegComponentState.BuildAll(Frame);

        while (true) {
            var marker = _cursor.ReadMarker();
            switch (marker) {
                case JpegMarkers.Sos:
                    DecodeScan();
                    break;
                case JpegMarkers.Eoi:
                    return;
                default:
                    ReadMetadataSegment(marker);
                    break;
            }
        }
    }

    private void ReadMetadataSegment(byte marker)
    {
        switch (marker) {
            case JpegMarkers.Dqt:
                ReadDqt();
                break;
            case JpegMarkers.Dht:
                ReadDht();
                break;
            case JpegMarkers.Dri:
                ReadDri();
                break;
            case JpegMarkers.Com:
                _comments.Add(System.Text.Encoding.Latin1.GetString(_cursor.ReadSegment()));
                break;
            case >= JpegMarkers.App0 and <= 0xEF:
                ReadAppSegment(marker);
                break;
            default:
                _cursor.ReadSegment();
                break;
        }
    }

    private void ReadFrameHeader(bool progressive)
    {
        var data = _cursor.ReadSegment();
        var precision = data[0];
        if (precision != 8) {
            throw new ImageFormatException("jpeg", "Unsupported.Jpeg.Precision", $"Only 8-bit JPEG sample precision is supported, found {precision}.");
        }

        var height = (data[1] << 8) | data[2];
        var width = (data[3] << 8) | data[4];
        if (width <= 0 || height <= 0) {
            throw new ImageFormatException("jpeg", "BadFrameHeader", "JPEG width and height must be positive.");
        }

        var componentCount = data[5];
        if (componentCount is not (1 or 3)) {
            throw new ImageFormatException("jpeg", "Unsupported.Jpeg.ComponentCount", $"Only grayscale (1) and YCbCr/RGB (3) component JPEGs are supported, found {componentCount}.");
        }

        var components = new JpegComponent[componentCount];
        var offset = 6;
        for (var i = 0; i < componentCount; i++) {
            var id = data[offset++];
            var sampling = data[offset++];
            var quantTableId = data[offset++];
            var hSampling = (byte)(sampling >> 4);
            var vSampling = (byte)(sampling & 0xF);
            if (hSampling is < 1 or > 4 || vSampling is < 1 or > 4) {
                throw new ImageFormatException("jpeg", "BadFrameHeader", $"Component sampling factors must be in [1,4], found H={hSampling} V={vSampling}.");
            }

            components[i] = new JpegComponent {
                Id = id,
                HSampling = hSampling,
                VSampling = vSampling,
                QuantTableId = quantTableId,
            };
        }

        Frame = new JpegFrameHeader {
            Progressive = progressive,
            Precision = precision,
            Width = width,
            Height = height,
            Components = components,
        };
    }

    private void ReadDqt()
    {
        var data = _cursor.ReadSegment();
        var offset = 0;
        while (offset < data.Length) {
            var control = data[offset++];
            var precision = control >> 4;
            var id = control & 0xF;

            var values = new ushort[64];
            for (var i = 0; i < 64; i++) {
                if (precision == 0) {
                    values[JpegZigZag.Order[i]] = data[offset++];
                }
                else {
                    values[JpegZigZag.Order[i]] = (ushort)((data[offset] << 8) | data[offset + 1]);
                    offset += 2;
                }
            }

            _quantTables[id] = new JpegQuantizationTable { Id = id, Values = values };
        }
    }

    private void ReadDht()
    {
        var data = _cursor.ReadSegment();
        var offset = 0;
        while (offset < data.Length) {
            var control = data[offset++];
            var isAc = (control >> 4) != 0;
            var id = control & 0xF;

            var bits = new byte[16];
            var total = 0;
            for (var i = 0; i < 16; i++) {
                bits[i] = data[offset++];
                total += bits[i];
            }

            var values = new byte[total];
            for (var i = 0; i < total; i++) {
                values[i] = data[offset++];
            }

            var spec = new JpegHuffmanSpec { Id = id, IsAc = isAc, Bits = bits, Values = values };
            var table = JpegHuffmanDecodeTable.Create(spec);
            var index = HuffmanTableIndex(id, isAc);
            _huffmanTables[index]?.Dispose();
            _huffmanTables[index] = table;
        }
    }

    private void ReadDri()
    {
        var data = _cursor.ReadSegment();
        _restartInterval = (data[0] << 8) | data[1];
    }

    private void ReadAppSegment(byte marker)
    {
        var data = _cursor.ReadSegment();
        _appSegments.Add(new JpegAppSegment { Marker = marker, Data = data });

        if (marker == JpegMarkers.App14 && data.Length >= 12 && data[0] == (byte)'A' && data[5] == (byte)'e') {
            AdobeTransformIsRaw = data[11] == 0;
        }
    }

    private void DecodeScan()
    {
        var data = _cursor.ReadSegment();
        var componentCount = data[0];
        if (componentCount == 0 || componentCount > Frame.Components.Count || data.Length != 4 + 2 * componentCount) {
            throw new InvalidDataException("Invalid JPEG scan component count or header length.");
        }
        var scanComponents = new JpegScanComponent[componentCount];
        var offset = 1;
        for (var i = 0; i < componentCount; i++) {
            var selector = data[offset++];
            var tables = data[offset++];
            scanComponents[i] = new JpegScanComponent {
                ComponentSelector = selector,
                DcTableSelector = (byte)(tables >> 4),
                AcTableSelector = (byte)(tables & 0xF),
            };
        }

        var spectralStart = data[offset++];
        var spectralEnd = data[offset++];
        var approximation = data[offset];
        if (!Frame.Progressive && (spectralStart != 0 || spectralEnd != 63 || approximation != 0)) {
            throw new InvalidDataException("Invalid sequential JPEG scan parameters.");
        }
        if (Frame.Progressive && (spectralStart > spectralEnd || spectralEnd > 63 ||
            (spectralStart == 0 ? spectralEnd != 0 : componentCount != 1) ||
            (approximation & 15) > 13 || (approximation >> 4) > 13 ||
            ((approximation >> 4) != 0 && (approximation >> 4) != (approximation & 15) + 1))) {
            throw new InvalidDataException("Invalid progressive JPEG scan parameters.");
        }
        var scan = new JpegScanHeader {
            Components = scanComponents,
            SpectralStart = spectralStart,
            SpectralEnd = spectralEnd,
            SuccessiveApproxHigh = (byte)(approximation >> 4),
            SuccessiveApproxLow = (byte)(approximation & 0xF),
        };

        var active = new (JpegComponentState Component, JpegScanComponent Scan)[componentCount];
        for (var i = 0; i < componentCount; i++) {
            active[i] = (FindComponent(scanComponents[i].ComponentSelector), scanComponents[i]);
            active[i].Component.DcPredictor = 0;
        }

        if (!Frame.Progressive) {
            DecodeBaselineScan(active);
            return;
        }
        var reader = new JpegBitReader(_cursor);
        var eobRun = 0;

        if (active.Length > 1) {
            DecodeProgressiveInterleavedScan(reader, active, scan, ref eobRun);
        }
        else {
            DecodeProgressiveNonInterleavedScan(reader, active[0], scan, ref eobRun);
        }

        reader.FinishSegment();
    }

    private void DecodeBaselineScan((JpegComponentState Component, JpegScanComponent Scan)[] active)
    {
        var tables = new (JpegHuffmanDecodeTable Dc, JpegHuffmanDecodeTable Ac)[active.Length];
        for (var i = 0; i < active.Length; i++) {
            tables[i] = (GetHuffmanTable(active[i].Scan.DcTableSelector, isAc: false),
                GetHuffmanTable(active[i].Scan.AcTableSelector, isAc: true));
        }
        var unitsPerLine = active.Length == 1 ? active[0].Component.BlocksPerLine : JpegComponentState.CeilDiv(Frame.Width, 8 * Frame.HMax);
        var unitsPerColumn = active.Length == 1 ? active[0].Component.BlocksPerColumn : JpegComponentState.CeilDiv(Frame.Height, 8 * Frame.VMax);
        var totalUnits = unitsPerLine * unitsPerColumn;
        if (_restartInterval == 0) {
            var reader = new JpegBitReader(_cursor);
            DecodeBaselineRange(reader, active, tables, unitsPerLine, 0, totalUnits);
            reader.FinishSegment();
            return;
        }

        var (buffer, length, segmentStarts) = ReadRestartSegments();
        var restartInterval = _restartInterval;
        ExecutionScheduler.For(0, segmentStarts.Count, segmentIndex => {
            var start = segmentIndex * restartInterval;
            var end = Math.Min(start + restartInterval, totalUnits);
            var offset = segmentStarts[segmentIndex];
            using var stream = new MemoryStream(buffer, offset, length - offset, writable: false);
            var reader = new JpegBitReader(new JpegByteCursor(stream));
            DecodeBaselineRange(reader, active, tables, unitsPerLine, start, end);
        });
    }

    private static void DecodeBaselineRange(
        JpegBitReader reader,
        (JpegComponentState Component, JpegScanComponent Scan)[] active,
        (JpegHuffmanDecodeTable Dc, JpegHuffmanDecodeTable Ac)[] tables,
        int unitsPerLine,
        int start,
        int end)
    {
        var row = start / unitsPerLine;
        var col = start % unitsPerLine;
        if (active.Length == 1) {
            var component = active[0].Component;
            var predictor = 0;
            for (var unit = start; unit < end; unit++) {
                BaselineBlockDecoder.DecodeBlock(reader, component.Coefficients, component.BlockOffset(row, col), ref predictor, tables[0].Dc, tables[0].Ac);
                if (++col == unitsPerLine) {
                    col = 0;
                    row++;
                }
            }
            return;
        }

        Span<int> predictors = stackalloc int[active.Length];
        predictors.Clear();
        for (var unit = start; unit < end; unit++) {
            for (var i = 0; i < active.Length; i++) {
                var component = active[i].Component;
                var hSampling = component.Component.HSampling;
                var vSampling = component.Component.VSampling;
                for (var v = 0; v < vSampling; v++) {
                    for (var h = 0; h < hSampling; h++) {
                        var offset = component.BlockOffset(row * vSampling + v, col * hSampling + h);
                        BaselineBlockDecoder.DecodeBlock(reader, component.Coefficients, offset, ref predictors[i], tables[i].Dc, tables[i].Ac);
                    }
                }
            }
            if (++col == unitsPerLine) {
                col = 0;
                row++;
            }
        }
    }

    private const int k_SegmentRefillPadding = 8;

    private (byte[] Buffer, int Length, List<int> SegmentStarts) ReadRestartSegments()
    {
        var buffer = new byte[8192];
        var length = 0;
        var searched = 0;
        var segmentStarts = new List<int> { 0 };

        while (true) {
            while (true) {
                var relativeIndex = buffer.AsSpan(searched, length - searched).IndexOf(JpegMarkers.Prefix);
                if (relativeIndex < 0) {
                    searched = length;
                    break;
                }

                var ffIndex = searched + relativeIndex;
                if (ffIndex + 1 >= length) {
                    searched = ffIndex;
                    break;
                }

                var next = buffer[ffIndex + 1];
                if (next == JpegMarkers.Padding) {
                    searched = ffIndex + 2;
                    continue;
                }

                if (JpegMarkers.IsRestart(next)) {
                    segmentStarts.Add(ffIndex + 2);
                    searched = ffIndex + 2;
                    continue;
                }

                _cursor.PushBackBytes(buffer.AsSpan(ffIndex, length - ffIndex));

                var paddedLength = ffIndex + k_SegmentRefillPadding;
                if (buffer.Length < paddedLength) {
                    Array.Resize(ref buffer, paddedLength);
                }

                return (buffer, paddedLength, segmentStarts);
            }

            if (length == buffer.Length) {
                Array.Resize(ref buffer, buffer.Length * 2);
            }

            var read = _cursor.ReadBulk(buffer.AsSpan(length));
            if (read == 0) {
                throw new ImageFormatException("jpeg", "BadEntropyData", "JPEG scan data ended before a terminating marker was found.");
            }

            length += read;
        }
    }

    private void DecodeProgressiveInterleavedScan(JpegBitReader reader, (JpegComponentState Component, JpegScanComponent Scan)[] active, JpegScanHeader scan, ref int eobRun)
    {
        var mcusPerLine = JpegComponentState.CeilDiv(Frame.Width, 8 * Frame.HMax);
        var mcusPerColumn = JpegComponentState.CeilDiv(Frame.Height, 8 * Frame.VMax);
        var totalMcus = mcusPerLine * mcusPerColumn;

        var mcuIndex = 0;
        var restartMarker = JpegMarkers.Rst0;

        for (var mcuRow = 0; mcuRow < mcusPerColumn; mcuRow++) {
            for (var mcuCol = 0; mcuCol < mcusPerLine; mcuCol++) {
                foreach (var (component, scanComponent) in active) {
                    for (var v = 0; v < component.Component.VSampling; v++) {
                        for (var h = 0; h < component.Component.HSampling; h++) {
                            var blockRow = (mcuRow * component.Component.VSampling) + v;
                            var blockCol = (mcuCol * component.Component.HSampling) + h;
                            DecodeProgressiveBlock(reader, component, blockRow, blockCol, scanComponent, scan, ref eobRun);
                        }
                    }
                }

                mcuIndex++;
                if (_restartInterval > 0 && mcuIndex % _restartInterval == 0 && mcuIndex < totalMcus) {
                    reader.ConsumeRestartMarker(restartMarker);
                    restartMarker = restartMarker == JpegMarkers.Rst7 ? JpegMarkers.Rst0 : (byte)(restartMarker + 1);
                    foreach (var (component, _) in active) {
                        component.DcPredictor = 0;
                    }

                    eobRun = 0;
                }
            }
        }
    }

    private void DecodeProgressiveNonInterleavedScan(JpegBitReader reader, (JpegComponentState Component, JpegScanComponent Scan) active, JpegScanHeader scan, ref int eobRun)
    {
        var (component, scanComponent) = active;
        var total = component.BlocksPerLine * component.BlocksPerColumn;
        var blockIndex = 0;
        var restartMarker = JpegMarkers.Rst0;

        for (var blockRow = 0; blockRow < component.BlocksPerColumn; blockRow++) {
            for (var blockCol = 0; blockCol < component.BlocksPerLine; blockCol++) {
                DecodeProgressiveBlock(reader, component, blockRow, blockCol, scanComponent, scan, ref eobRun);

                blockIndex++;
                if (_restartInterval > 0 && blockIndex % _restartInterval == 0 && blockIndex < total) {
                    reader.ConsumeRestartMarker(restartMarker);
                    restartMarker = restartMarker == JpegMarkers.Rst7 ? JpegMarkers.Rst0 : (byte)(restartMarker + 1);
                    component.DcPredictor = 0;
                    eobRun = 0;
                }
            }
        }
    }

    private void DecodeProgressiveBlock(
        JpegBitReader reader,
        JpegComponentState component,
        int blockRow,
        int blockCol,
        JpegScanComponent scanComponent,
        JpegScanHeader scan,
        ref int eobRun)
    {
        var blockOffset = component.BlockOffset(blockRow, blockCol);

        if (scan.SpectralStart == 0) {
            if (scan.SuccessiveApproxHigh == 0) {
                ProgressiveBlockDecoder.DecodeDcFirst(reader, component, blockOffset, GetHuffmanTable(scanComponent.DcTableSelector, isAc: false), scan.SuccessiveApproxLow);
            }
            else {
                ProgressiveBlockDecoder.DecodeDcRefine(reader, component, blockOffset, scan.SuccessiveApproxLow);
            }

            return;
        }

        var acTable = GetHuffmanTable(scanComponent.AcTableSelector, isAc: true);
        if (scan.SuccessiveApproxHigh == 0) {
            ProgressiveBlockDecoder.DecodeAcFirst(reader, component, blockOffset, acTable, scan.SpectralStart, scan.SpectralEnd, scan.SuccessiveApproxLow, ref eobRun);
        }
        else {
            ProgressiveBlockDecoder.DecodeAcRefine(reader, component, blockOffset, acTable, scan.SpectralStart, scan.SpectralEnd, scan.SuccessiveApproxLow, ref eobRun);
        }
    }

    private static int HuffmanTableIndex(int id, bool isAc) => id + (isAc ? 16 : 0);

    private JpegHuffmanDecodeTable GetHuffmanTable(int id, bool isAc) => _huffmanTables[HuffmanTableIndex(id, isAc)]
        ?? throw new ImageFormatException("jpeg", "BadTableReference", $"JPEG scan references undefined {(isAc ? "AC" : "DC")} Huffman table {id}.");

    private JpegComponentState FindComponent(byte id)
    {
        foreach (var component in _components) {
            if (component.Component.Id == id) {
                return component;
            }
        }

        throw new ImageFormatException("jpeg", "BadScanHeader", $"Scan references unknown component id {id}.");
    }
}
