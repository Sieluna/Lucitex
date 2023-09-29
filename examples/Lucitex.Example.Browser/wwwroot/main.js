import { dotnet } from './_framework/dotnet.js';

const log = document.getElementById('log');
const sourceInput = document.getElementById('source');
const targetSelect = document.getElementById('target');
const convertButton = document.getElementById('convert');

function write(message) {
    log.textContent = message;
}

const { getAssemblyExports, getConfig, runMain } = await dotnet.create();
const config = getConfig();
const exports = await getAssemblyExports(config.mainAssemblyName);
const convert = exports.Lucitex.Example.Browser.ImageConversion.Convert;

convertButton.disabled = false;
write('Ready. Pick a source image and a target format.');

convertButton.addEventListener('click', async () => {
    const file = sourceInput.files?.[0];
    if (!file) {
        write('Pick a source image first.');
        return;
    }

    const sourceExtension = file.name.slice(file.name.lastIndexOf('.'));
    const targetExtension = `.${targetSelect.value}`;

    try {
        const bytes = new Uint8Array(await file.arrayBuffer());
        const result = convert(bytes, sourceExtension, targetExtension);
        const blob = new Blob([result], { type: 'application/octet-stream' });
        const url = URL.createObjectURL(blob);
        const name = file.name.slice(0, file.name.lastIndexOf('.')) + targetExtension;

        const link = document.createElement('a');
        link.href = url;
        link.download = name;
        link.click();
        URL.revokeObjectURL(url);

        write(`Converted ${file.name} -> ${name} (${result.length} bytes).`);
    } catch (error) {
        write(String(error));
    }
});

await runMain();
