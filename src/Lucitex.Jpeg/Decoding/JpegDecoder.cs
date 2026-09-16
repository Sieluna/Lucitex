using Lucitex.Core.Execution;
using Lucitex.Jpeg.Format;

namespace Lucitex.Jpeg.Decoding;

internal sealed class JpegDecoder
{
    private readonly JpegByteCursor _cursor;
    private readonly Dictionary<int, JpegQuantizationTable> _quantTables = new();
    private readonly Dictionary<int, JpegHuffmanDecodeTable> _dcHuffmanTables = new();
    private readonly Dictionary<int, JpegHuffmanDecodeTable> _acHuffmanTables = new();
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
                case JpegMarkers.Eoi:
                    throw new ImageFormatException("jpeg", "MissingFrameHeader", "JPEG stream reached EOI before a frame header was found.");
                case >= JpegMarkers.App0 and <= 0xEF:
                    ReadAppSegment(marker);
                    break;
                default:
                    _cursor.ReadSegment();
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
                case JpegMarkers.Sos:
                    DecodeScan();
                    break;
                case JpegMarkers.Eoi:
                    return;
                default:
                    _cursor.ReadSegment();
                    break;
            }
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
            if (isAc) {
                _acHuffmanTables[id] = new JpegHuffmanDecodeTable(spec);
            }
            else {
                _dcHuffmanTables[id] = new JpegHuffmanDecodeTable(spec);
            }
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

        var reader = new JpegBitReader(_cursor);
        var eobRun = 0;

        if (active.Length > 1) {
            DecodeInterleavedScan(reader, active, scan, ref eobRun);
        }
        else {
            DecodeNonInterleavedScan(reader, active[0], scan, ref eobRun);
        }

        reader.FinishSegment();
    }

    private void DecodeInterleavedScan(JpegBitReader reader, (JpegComponentState Component, JpegScanComponent Scan)[] active, JpegScanHeader scan, ref int eobRun)
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
                            DecodeOneBlock(reader, component, blockRow, blockCol, scanComponent, scan, ref eobRun);
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

    private void DecodeNonInterleavedScan(JpegBitReader reader, (JpegComponentState Component, JpegScanComponent Scan) active, JpegScanHeader scan, ref int eobRun)
    {
        var (component, scanComponent) = active;
        var total = component.BlocksPerLine * component.BlocksPerColumn;
        var blockIndex = 0;
        var restartMarker = JpegMarkers.Rst0;

        for (var blockRow = 0; blockRow < component.BlocksPerColumn; blockRow++) {
            for (var blockCol = 0; blockCol < component.BlocksPerLine; blockCol++) {
                DecodeOneBlock(reader, component, blockRow, blockCol, scanComponent, scan, ref eobRun);

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

    private void DecodeOneBlock(
        JpegBitReader reader,
        JpegComponentState component,
        int blockRow,
        int blockCol,
        JpegScanComponent scanComponent,
        JpegScanHeader scan,
        ref int eobRun)
    {
        var blockOffset = component.BlockOffset(blockRow, blockCol);

        if (!Frame.Progressive) {
            BaselineBlockDecoder.DecodeBlock(reader, component, blockOffset, GetDcTable(scanComponent.DcTableSelector), GetAcTable(scanComponent.AcTableSelector));
            return;
        }

        if (scan.SpectralStart == 0) {
            if (scan.SuccessiveApproxHigh == 0) {
                ProgressiveBlockDecoder.DecodeDcFirst(reader, component, blockOffset, GetDcTable(scanComponent.DcTableSelector), scan.SuccessiveApproxLow);
            }
            else {
                ProgressiveBlockDecoder.DecodeDcRefine(reader, component, blockOffset, scan.SuccessiveApproxLow);
            }

            return;
        }

        var acTable = GetAcTable(scanComponent.AcTableSelector);
        if (scan.SuccessiveApproxHigh == 0) {
            ProgressiveBlockDecoder.DecodeAcFirst(reader, component, blockOffset, acTable, scan.SpectralStart, scan.SpectralEnd, scan.SuccessiveApproxLow, ref eobRun);
        }
        else {
            ProgressiveBlockDecoder.DecodeAcRefine(reader, component, blockOffset, acTable, scan.SpectralStart, scan.SpectralEnd, scan.SuccessiveApproxLow, ref eobRun);
        }
    }

    private JpegHuffmanDecodeTable GetDcTable(int id) => _dcHuffmanTables.TryGetValue(id, out var table)
        ? table
        : throw new ImageFormatException("jpeg", "BadTableReference", $"JPEG scan references undefined DC Huffman table {id}.");

    private JpegHuffmanDecodeTable GetAcTable(int id) => _acHuffmanTables.TryGetValue(id, out var table)
        ? table
        : throw new ImageFormatException("jpeg", "BadTableReference", $"JPEG scan references undefined AC Huffman table {id}.");

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
