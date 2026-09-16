namespace Lucitex.Jpeg.Format;

internal static class JpegMarkers
{
    public const byte Prefix = 0xFF;
    public const byte Padding = 0x00;

    public const byte Soi = 0xD8;
    public const byte Eoi = 0xD9;

    public const byte Sof0 = 0xC0;
    public const byte Sof1 = 0xC1;
    public const byte Sof2 = 0xC2;

    public const byte Dht = 0xC4;
    public const byte Dqt = 0xDB;
    public const byte Dri = 0xDD;
    public const byte Sos = 0xDA;

    public const byte Rst0 = 0xD0;
    public const byte Rst7 = 0xD7;

    public const byte App0 = 0xE0;
    public const byte App14 = 0xEE;
    public const byte Com = 0xFE;

    public static bool IsRestart(byte marker) => marker >= Rst0 && marker <= Rst7;

    public static bool IsStandaloneMarker(byte marker) => marker is Soi or Eoi || IsRestart(marker) || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7);
}
