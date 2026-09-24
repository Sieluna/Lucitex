import { dotnet } from './_framework/dotnet.js';

try {
    const { getAssemblyExports, getConfig, runMain } = await dotnet.create();
    const config = getConfig();
    const exports = await getAssemblyExports(config.mainAssemblyName);
    const api = exports.Lucitex.Example.Browser.ImageConversion;
    await runMain();
    self.addEventListener('message', ({ data }) => {
        if (data.type !== 'convert') return;
        try {
            const started = performance.now();
            const result = api.Convert(new Uint8Array(data.bytes), data.sourceExtension,
                data.targetExtension, JSON.stringify(data.options));
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
