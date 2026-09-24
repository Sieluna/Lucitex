const form = document.getElementById('conversion');
const log = document.getElementById('log');
const sourceInput = document.getElementById('source');
const targetSelect = document.getElementById('target');
const optionsPanel = document.getElementById('options');
const optionsFields = document.getElementById('option-fields');
const resetButton = document.getElementById('reset');
const convertButton = document.getElementById('convert');
const download = document.getElementById('download');
const settings = new Map();
let formats = [];
let ready = false;
let pending = null;
let downloadUrl = null;

function setBusy(busy) {
    form.setAttribute('aria-busy', String(busy));
    sourceInput.disabled = !ready || busy;
    targetSelect.disabled = !ready || busy;
    optionsPanel.disabled = !ready || busy;
    convertButton.disabled = !ready || busy;
    convertButton.textContent = busy ? 'Converting…' : 'Convert and download';
}

function clearDownload() {
    if (downloadUrl) URL.revokeObjectURL(downloadUrl);
    downloadUrl = null;
    download.hidden = true;
    download.removeAttribute('href');
}

function renderOptions(reset = false) {
    const format = formats.find(item => item.id === targetSelect.value);
    if (!format) return;
    if (reset || !settings.has(format.id)) {
        settings.set(format.id, Object.fromEntries(format.parameters.map(option => [option.id, option.defaultValue])));
    }
    const values = settings.get(format.id);
    const rows = [];
    const updateVisibility = () => {
        for (const { row, option } of rows) {
            const visible = !option.condition || values[option.condition.id] === option.condition.value;
            row.hidden = !visible;
            for (const input of row.querySelectorAll('input, select')) input.disabled = !visible;
        }
    };
    optionsFields.replaceChildren();
    resetButton.disabled = format.parameters.length === 0;
    if (format.parameters.length === 0) {
        const message = document.createElement('p');
        message.className = 'hint';
        message.textContent = 'This encoder has no adjustable options.';
        optionsFields.append(message);
    }
    for (const option of format.parameters) {
        const row = document.createElement('div');
        row.className = 'option';
        const label = document.createElement('label');
        const id = `option-${option.id}`;
        label.htmlFor = id;
        label.textContent = option.label;
        const help = document.createElement('p');
        help.id = `${id}-help`;
        help.className = 'hint';
        help.textContent = option.description;
        let control;
        let slider;
        if (option.kind === 'Choice') {
            control = document.createElement('select');
            for (const choice of option.choices) {
                control.add(new Option(choice.label, choice.value));
            }
            control.value = values[option.id];
        } else if (option.kind === 'Boolean') {
            control = document.createElement('input');
            control.type = 'checkbox';
            control.checked = values[option.id] === 'true';
            row.classList.add('toggle');
        } else if (option.kind === 'Integer') {
            control = document.createElement('input');
            control.type = 'number';
            control.step = '1';
            control.required = true;
            if (option.minimum !== null) control.min = String(option.minimum);
            if (option.maximum !== null) control.max = String(option.maximum);
            control.value = values[option.id];
            if (option.minimum !== null && option.maximum !== null && option.maximum - option.minimum <= 1000) {
                slider = document.createElement('input');
                slider.type = 'range';
                slider.min = control.min;
                slider.max = control.max;
                slider.step = control.step;
                slider.value = control.value;
                slider.setAttribute('aria-label', option.label);
                slider.setAttribute('aria-describedby', help.id);
                slider.addEventListener('input', () => {
                    control.value = slider.value;
                    values[option.id] = slider.value;
                    updateVisibility();
                });
            }
        } else {
            throw new Error(`Unsupported option type: ${option.kind}`);
        }
        control.id = id;
        control.setAttribute('aria-describedby', help.id);
        control.addEventListener('input', () => {
            values[option.id] = option.kind === 'Boolean' ? String(control.checked) : control.value;
            if (slider && control.validity.valid) slider.value = control.value;
            updateVisibility();
        });
        row.append(label);
        const inputs = document.createElement('div');
        inputs.className = slider ? 'numeric-option' : 'option-input';
        if (slider) inputs.append(slider);
        inputs.append(control);
        row.append(inputs, help);
        optionsFields.append(row);
        rows.push({ row, option });
    }
    updateVisibility();
}

const worker = new Worker(new URL('./conversion-worker.js', import.meta.url), { type: 'module' });
worker.addEventListener('message', ({ data }) => {
    if (data.type === 'ready') {
        try {
            formats = data.formats;
            targetSelect.replaceChildren();
            for (const format of formats) targetSelect.add(new Option(format.id.toUpperCase(), format.id));
            sourceInput.accept = [...new Set(formats.flatMap(format => format.extensions))].join(',');
            renderOptions();
            ready = true;
            setBusy(false);
            log.textContent = 'Ready. Pick a source image and configure the target encoder.';
        } catch (error) {
            log.textContent = String(error);
        }
    } else if (data.type === 'result' && pending) {
        const result = new Uint8Array(data.bytes);
        downloadUrl = URL.createObjectURL(new Blob([result], { type: 'application/octet-stream' }));
        download.href = downloadUrl;
        download.download = pending.name;
        download.textContent = `Download ${pending.name}`;
        download.hidden = false;
        const ratio = pending.size > 0 ? `${(result.length / pending.size * 100).toFixed(1)}%` : 'n/a';
        const applied = Object.entries(pending.options).map(([key, value]) => `${key}=${value}`).join(', ');
        log.textContent = `${pending.sourceName} → ${pending.name}\n` +
            `Input: ${pending.size.toLocaleString()} bytes · Output: ${result.length.toLocaleString()} bytes\n` +
            `Output / input: ${ratio} · Conversion: ${Math.round(data.elapsedMs).toLocaleString()} ms` +
            (applied ? `\nSettings: ${applied}` : '');
        pending = null;
        setBusy(false);
        download.click();
    } else if (data.type === 'error') {
        log.textContent = data.message;
        pending = null;
        if (data.fatal) ready = false;
        setBusy(false);
    }
});
worker.addEventListener('error', event => {
    log.textContent = `Conversion worker failed: ${event.message}. Reload the page to retry.`;
    ready = false;
    pending = null;
    setBusy(false);
});

targetSelect.addEventListener('change', () => renderOptions());
resetButton.addEventListener('click', () => renderOptions(true));
form.addEventListener('submit', async event => {
    event.preventDefault();
    if (!ready || pending || !form.reportValidity()) return;
    const file = sourceInput.files?.[0];
    if (!file) return;
    const format = formats.find(item => item.id === targetSelect.value);
    const dot = file.name.lastIndexOf('.');
    if (dot < 0) {
        log.textContent = 'The source filename must include its image extension.';
        return;
    }
    pending = {
        name: file.name.slice(0, dot) + format.extension,
        sourceName: file.name,
        size: file.size,
        options: Object.fromEntries(format.parameters
            .filter(option => !option.condition || settings.get(format.id)[option.condition.id] === option.condition.value)
            .map(option => [option.id, settings.get(format.id)[option.id]])),
    };
    clearDownload();
    setBusy(true);
    log.textContent = 'Converting with the selected encoder settings…';
    try {
        const bytes = await file.arrayBuffer();
        worker.postMessage({ type: 'convert', bytes, sourceExtension: file.name.slice(dot),
            targetExtension: format.extension, options: pending.options }, [bytes]);
    } catch (error) {
        log.textContent = String(error);
        pending = null;
        setBusy(false);
    }
});
window.addEventListener('pagehide', clearDownload);
