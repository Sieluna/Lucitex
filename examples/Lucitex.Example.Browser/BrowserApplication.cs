using System.Diagnostics;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using Lucitex.Example.Shared;

namespace Lucitex.Example.Browser;

internal static class BrowserApplication
{
    private static readonly JsonSerializerOptions s_JsonOptions = new() {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static async Task RunAsync()
    {
        BrowserInterop.Initialize(ImageConversion.GetFormats());
        BrowserInterop.SetBusy(false);
        BrowserInterop.SetStatus("Ready. Pick a source image and configure the target encoder.");

        while (true) {
            var eventName = await BrowserInterop.WaitForEventAsync();
            try {
                switch (eventName) {
                    case "source-change":
                        await LoadPreviewAsync();
                        break;
                    case "convert":
                        await ConvertAsync();
                        break;
                }
            }
            catch (Exception error) {
                BrowserInterop.SetStatus(error.Message);
                BrowserInterop.SetBusy(false);
            }
        }
    }

    private static async Task LoadPreviewAsync()
    {
        var sourceJson = await BrowserInterop.ReadSourceAsync();
        var source = JsonSerializer.Deserialize<SourceInput>(sourceJson, s_JsonOptions);
        if (source is null || source.BytesBase64.Length == 0) {
            return;
        }

        var sourceBytes = System.Convert.FromBase64String(source.BytesBase64);
        var previewBytes = await Task.Run(
            () => ImageConversion.GetPreview(sourceBytes, source.Extension));
        BrowserInterop.ShowSourcePreview(
            System.Convert.ToBase64String(previewBytes),
            source.RequestId);
    }

    private static async Task ConvertAsync()
    {
        var inputJson = await BrowserInterop.ReadConversionInputAsync();
        var input = JsonSerializer.Deserialize<ConversionInput>(inputJson, s_JsonOptions)
            ?? throw new InvalidOperationException("The conversion request was empty.");

        var dot = input.SourceName.LastIndexOf('.');
        if (dot < 0) {
            throw new InvalidOperationException("The source filename must include its image extension.");
        }
        if ((input.ResizeWidth > 0) != (input.ResizeHeight > 0)) {
            throw new InvalidOperationException("Set both resize width and height, or leave both blank.");
        }

        var targetSizeBytes = ParseTargetSize(input.TargetSize);
        var target = Formats.Resolve(input.TargetFormat);
        var optionsJson = JsonSerializer.Serialize(input.Options);
        var sourceBytes = System.Convert.FromBase64String(input.BytesBase64);

        BrowserInterop.ClearResult();
        BrowserInterop.SetBusy(true);
        BrowserInterop.SetStatus("Converting with the selected encoder settings…");

        var timer = Stopwatch.StartNew();
        var result = await Task.Run(() => ImageConversion.Convert(
            sourceBytes,
            input.SourceExtension,
            target.Extension,
            optionsJson,
            input.CropX,
            input.CropY,
            input.CropWidth,
            input.CropHeight,
            input.ResizeWidth,
            input.ResizeHeight,
            targetSizeBytes));
        timer.Stop();

        var resultName = input.SourceName[..dot] + target.Extension;
        var metadata = new ConversionResult(
            input.SourceName,
            resultName,
            input.SourceSize,
            result.Length,
            timer.ElapsedMilliseconds,
            input.Options,
            input.CropX,
            input.CropY,
            input.CropWidth,
            input.CropHeight,
            input.NaturalWidth,
            input.NaturalHeight,
            input.ResizeWidth,
            input.ResizeHeight,
            targetSizeBytes);
        BrowserInterop.ShowResult(
            System.Convert.ToBase64String(result),
            JsonSerializer.Serialize(metadata));
        BrowserInterop.SetBusy(false);
    }

    private static int ParseTargetSize(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0) {
            return 0;
        }

        var match = System.Text.RegularExpressions.Regex.Match(
            trimmed,
            @"^(\d+(?:\.\d+)?)\s*(K|KB|M|MB|B)?$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        if (!match.Success
            || !double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.AllowDecimalPoint,
                System.Globalization.CultureInfo.InvariantCulture, out var amount)) {
            throw new InvalidOperationException($"'{text}' must look like '10K', '2MB' or a raw byte count.");
        }

        var unit = match.Groups[2].Value.ToUpperInvariant();
        var multiplier = unit is "K" or "KB" ? 1024d
            : unit is "M" or "MB" ? 1024d * 1024d
            : 1d;
        var bytes = Math.Round(amount * multiplier, MidpointRounding.AwayFromZero);
        if (bytes <= 0 || bytes > int.MaxValue) {
            throw new InvalidOperationException($"'{text}' must be a positive size no greater than 2 GB.");
        }
        return (int)bytes;
    }

    private sealed record SourceInput(
        int RequestId,
        string Extension,
        string BytesBase64);

    private sealed record ConversionInput(
        string SourceName,
        long SourceSize,
        string SourceExtension,
        string BytesBase64,
        string TargetFormat,
        Dictionary<string, string> Options,
        int CropX,
        int CropY,
        int CropWidth,
        int CropHeight,
        int NaturalWidth,
        int NaturalHeight,
        int ResizeWidth,
        int ResizeHeight,
        string TargetSize);

    private sealed record ConversionResult(
        string SourceName,
        string Name,
        long InputSize,
        int OutputSize,
        long ElapsedMs,
        Dictionary<string, string> Options,
        int CropX,
        int CropY,
        int CropWidth,
        int CropHeight,
        int NaturalWidth,
        int NaturalHeight,
        int ResizeWidth,
        int ResizeHeight,
        int TargetSizeBytes);
}

internal static partial class BrowserInterop
{
    [JSImport("initialize", "main.js")]
    public static partial void Initialize(string formatsJson);

    [JSImport("setBusy", "main.js")]
    public static partial void SetBusy(bool busy);

    [JSImport("setStatus", "main.js")]
    public static partial void SetStatus(string message);

    [JSImport("clearResult", "main.js")]
    public static partial void ClearResult();

    [JSImport("showSourcePreview", "main.js")]
    public static partial void ShowSourcePreview(string bytesBase64, int requestId);

    [JSImport("showResult", "main.js")]
    public static partial void ShowResult(string bytesBase64, string metadataJson);

    [JSImport("waitForEvent", "main.js")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    public static partial Task<string> WaitForEventAsync();

    [JSImport("readSource", "main.js")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    public static partial Task<string> ReadSourceAsync();

    [JSImport("readConversionInput", "main.js")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    public static partial Task<string> ReadConversionInputAsync();
}
