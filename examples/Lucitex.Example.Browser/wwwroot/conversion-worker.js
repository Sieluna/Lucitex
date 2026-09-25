import { dotnet } from './_framework/dotnet.js';

try {
    const { getAssemblyExports, getConfig, runMain } = await dotnet.create();
    const config = getConfig();
    const exports = await getAssemblyExports(config.mainAssemblyName);
    const api = exports.Lucitex.Example.Browser.ImageConversion;
    await runMain();
    self.addEventListener('message', ({ data }) => {
        if (data.type === 'preview') {
            try {
                const result = api.GetPreview(new Uint8Array(data.bytes), data.sourceExtension);
                const bytes = Uint8Array.from(result);
                self.postMessage({ type: 'preview-result', requestId: data.requestId, bytes: bytes.buffer }, [bytes.buffer]);
            } catch (error) {
                self.postMessage({ type: 'error', message: String(error), fatal: false });
            }
            return;
        }
        if (data.type !== 'convert') return;
        try {
            const started = performance.now();
            const result = api.Convert(new Uint8Array(data.bytes), data.sourceExtension,
                data.targetExtension, JSON.stringify(data.options),
                data.cropX ?? 0, data.cropY ?? 0, data.cropWidth ?? 0, data.cropHeight ?? 0,
                data.resizeWidth ?? 0, data.resizeHeight ?? 0, data.targetSizeBytes ?? 0);
            const bytes = Uint8Array.from(result);
            self.postMessage({ type: 'result', bytes: bytes.buffer, elapsedMs: performance.now() - started }, [bytes.buffer]);
        } catch (error) {
            self.postMessage({ type: 'error', message: String(error), fatal: false });
        }
    });
    self.postMessage({ type: 'ready', formats: JSON.parse(api.GetFormats()) });
} catch (error) {
    self.postMessage({ type: 'error', message: `Unable to load the .NET runtime: ${error}`, fatal: true });
}
