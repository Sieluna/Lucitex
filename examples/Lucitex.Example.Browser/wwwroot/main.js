const form = document.getElementById('conversion');
const log = document.getElementById('log');
const dropzone = document.getElementById('dropzone');
const sourceInput = document.getElementById('source');
const editor = document.getElementById('editor');
const canvas = document.getElementById('canvas');
const ctx = canvas.getContext('2d');
const cropReadout = document.getElementById('crop-readout');
const cropXInput = document.getElementById('crop-x');
const cropYInput = document.getElementById('crop-y');
const cropWInput = document.getElementById('crop-w');
const cropHInput = document.getElementById('crop-h');
const ratioButtons = [...document.querySelectorAll('.ratio-btn')];
const resetCropButton = document.getElementById('reset-crop');
const resultTable = document.getElementById('result-table');
const previewAfter = document.getElementById('preview-after');
const afterPlaceholder = document.getElementById('after-placeholder');
const afterMeta = document.getElementById('after-meta');
const targetSelect = document.getElementById('target');
const resizeFieldset = document.getElementById('resize-fieldset');
const resizeWidthInput = document.getElementById('resize-width');
const resizeHeightInput = document.getElementById('resize-height');
const targetSizeInput = document.getElementById('target-size');
const optionsPanel = document.getElementById('options');
const optionsFields = document.getElementById('option-fields');
const resetButton = document.getElementById('reset');
const convertButton = document.getElementById('convert');
const download = document.getElementById('download');
const RENDERABLE_EXTENSIONS = new Set(['.png', '.jpg', '.jpeg', '.webp']);

function formatBytes(bytes) {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

function extensionOf(name) {
    const dot = name.lastIndexOf('.');
    return dot >= 0 ? name.slice(dot).toLowerCase() : '';
}

function mimeFor(extension) {
    if (extension === '.jpg' || extension === '.jpeg') return 'image/jpeg';
    if (extension === '.webp') return 'image/webp';
    return 'image/png';
}

function parseTargetSize(text) {
    const trimmed = text.trim();
    if (!trimmed) return 0;
    const match = /^(\d+(?:\.\d+)?)\s*(K|KB|M|MB|B)?$/i.exec(trimmed);
    if (!match) throw new Error(`'${text}' must look like '10K', '2MB' or a raw byte count.`);
    const amount = Number(match[1]);
    const unit = (match[2] ?? '').toUpperCase();
    const multiplier = unit === 'K' || unit === 'KB' ? 1024 : unit === 'M' || unit === 'MB' ? 1024 * 1024 : 1;
    const bytes = Math.round(amount * multiplier);
    if (bytes <= 0) throw new Error(`'${text}' must be a positive size.`);
    return bytes;
}

const settings = new Map();
let formats = [];
let ready = false;
let pending = null;
let downloadUrl = null;
let previewRequestId = 0;

let previewBitmap = null;
let naturalWidth = 0;
let naturalHeight = 0;
let canvasScale = 1;
let crop = null; // { x, y, width, height } in natural pixels, or null before an image loads
let drag = null; // { mode: 'move' | 'nw' | 'ne' | 'sw' | 'se', anchor: {x,y,width,height}, startX, startY }

const MIN_CROP_NATURAL = 8;
const MAX_CANVAS_WIDTH = 480;
const MAX_CANVAS_HEIGHT = 360;
const HANDLE_SIZE = 10;
const HANDLE_HIT = 10;

function setBusy(busy) {
    form.setAttribute('aria-busy', String(busy));
    sourceInput.disabled = !ready || busy;
    editor.disabled = !ready || busy;
    targetSelect.disabled = !ready || busy;
    resizeFieldset.disabled = !ready || busy;
    optionsPanel.disabled = !ready || busy;
    convertButton.disabled = !ready || busy;
    convertButton.textContent = busy ? 'Converting…' : 'Convert and download';
}

function clearResult() {
    if (downloadUrl) URL.revokeObjectURL(downloadUrl);
    downloadUrl = null;
    download.hidden = true;
    download.removeAttribute('href');
    previewAfter.hidden = true;
    previewAfter.removeAttribute('src');
    afterPlaceholder.hidden = false;
    afterPlaceholder.textContent = 'Not converted yet';
    afterMeta.textContent = '';
    resultTable.hidden = true;
}

function redraw() {
    if (!previewBitmap) return;
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    ctx.drawImage(previewBitmap, 0, 0, canvas.width, canvas.height);
    if (!crop) return;

    const box = cropBoxInBuffer();
    ctx.fillStyle = 'rgba(0, 0, 0, .5)';
    ctx.fillRect(0, 0, canvas.width, box.y);
    ctx.fillRect(0, box.y + box.height, canvas.width, canvas.height - (box.y + box.height));
    ctx.fillRect(0, box.y, box.x, box.height);
    ctx.fillRect(box.x + box.width, box.y, canvas.width - (box.x + box.width), box.height);

    ctx.strokeStyle = 'red';
    ctx.lineWidth = 2;
    ctx.setLineDash([6, 4]);
    ctx.strokeRect(box.x, box.y, box.width, box.height);
    ctx.setLineDash([]);

    ctx.fillStyle = 'red';
    for (const [hx, hy] of [[box.x, box.y], [box.x + box.width, box.y], [box.x, box.y + box.height], [box.x + box.width, box.y + box.height]]) {
        ctx.fillRect(hx - (HANDLE_SIZE / 2), hy - (HANDLE_SIZE / 2), HANDLE_SIZE, HANDLE_SIZE);
    }
}

function cropBoxInBuffer() {
    return { x: crop.x * canvasScale, y: crop.y * canvasScale, width: crop.width * canvasScale, height: crop.height * canvasScale };
}

function syncCropUi() {
    if (!crop) return;
    cropXInput.value = crop.x;
    cropYInput.value = crop.y;
    cropWInput.value = crop.width;
    cropHInput.value = crop.height;
    const isFullImage = crop.x === 0 && crop.y === 0 && crop.width === naturalWidth && crop.height === naturalHeight;
    cropReadout.textContent = isFullImage
        ? `${naturalWidth} × ${naturalHeight} (no crop)`
        : `${crop.width} × ${crop.height} at (${crop.x}, ${crop.y})`;
}

function setCrop(next) {
    crop = next;
    syncCropUi();
    redraw();
}

function setActiveRatio(ratio) {
    for (const button of ratioButtons) button.setAttribute('aria-pressed', String(button.dataset.ratio === ratio));
}

function resetCrop() {
    setActiveRatio('0');
    setCrop({ x: 0, y: 0, width: naturalWidth, height: naturalHeight });
}

function applyRatio(ratio) {
    setActiveRatio(ratio);
    if (ratio === '0') {
        setCrop({ x: 0, y: 0, width: naturalWidth, height: naturalHeight });
        return;
    }
    const [ratioW, ratioH] = ratio.split(':').map(Number);
    const targetRatio = ratioW / ratioH;
    const imageRatio = naturalWidth / naturalHeight;
    const width = imageRatio > targetRatio ? naturalHeight * targetRatio : naturalWidth;
    const height = imageRatio > targetRatio ? naturalHeight : naturalWidth / targetRatio;
    setCrop(clampCrop({ x: (naturalWidth - width) / 2, y: (naturalHeight - height) / 2, width, height }));
}

function applyManualCrop() {
    const width = Number(cropWInput.value) || crop.width;
    const height = Number(cropHInput.value) || crop.height;
    const x = cropXInput.value === '' ? crop.x : Number(cropXInput.value);
    const y = cropYInput.value === '' ? crop.y : Number(cropYInput.value);
    setActiveRatio('0');
    setCrop(clampCrop({ x, y, width, height }));
}

async function loadPreview(file) {
    editor.hidden = true;
    crop = null;
    previewBitmap?.close();
    previewBitmap = null;
    const extension = extensionOf(file.name);
    const requestId = ++previewRequestId;
    const bytes = await file.arrayBuffer();
    worker.postMessage({ type: 'preview', bytes, sourceExtension: extension, requestId }, [bytes]);

    return new Promise(resolve => {
        const onMessage = ({ data }) => {
            if (data.type !== 'preview-result' || data.requestId !== requestId) return;
            worker.removeEventListener('message', onMessage);
            const mime = RENDERABLE_EXTENSIONS.has(extension) ? mimeFor(extension) : 'image/png';
            createImageBitmap(new Blob([data.bytes], { type: mime })).then(bitmap => {
                if (requestId !== previewRequestId) { bitmap.close(); return; } // a newer file was picked meanwhile
                previewBitmap = bitmap;
                naturalWidth = bitmap.width;
                naturalHeight = bitmap.height;
                canvasScale = Math.min(1, MAX_CANVAS_WIDTH / naturalWidth, MAX_CANVAS_HEIGHT / naturalHeight);
                canvas.width = Math.round(naturalWidth * canvasScale);
                canvas.height = Math.round(naturalHeight * canvasScale);
                resetCrop();
                editor.hidden = false;
                resolve();
            }).catch(() => resolve()); // non-image or undecodable source: skip the crop tool silently
        };
        worker.addEventListener('message', onMessage);
    });
}

function bufferPointFromEvent(event) {
    const rect = canvas.getBoundingClientRect();
    return {
        x: (event.clientX - rect.left) * (canvas.width / rect.width),
        y: (event.clientY - rect.top) * (canvas.height / rect.height),
    };
}

function hitTest(point) {
    const box = cropBoxInBuffer();
    const corners = {
        nw: [box.x, box.y], ne: [box.x + box.width, box.y],
        sw: [box.x, box.y + box.height], se: [box.x + box.width, box.y + box.height],
    };
    for (const [name, [cx, cy]] of Object.entries(corners)) {
        if (Math.abs(point.x - cx) <= HANDLE_HIT && Math.abs(point.y - cy) <= HANDLE_HIT) return name;
    }
    if (point.x >= box.x && point.x <= box.x + box.width && point.y >= box.y && point.y <= box.y + box.height) return 'move';
    return null;
}

const CURSOR_BY_MODE = { move: 'move', nw: 'nwse-resize', se: 'nwse-resize', ne: 'nesw-resize', sw: 'nesw-resize' };

function clampCrop(box) {
    const width = Math.min(Math.max(box.width, MIN_CROP_NATURAL), naturalWidth);
    const height = Math.min(Math.max(box.height, MIN_CROP_NATURAL), naturalHeight);
    const x = Math.min(Math.max(box.x, 0), naturalWidth - width);
    const y = Math.min(Math.max(box.y, 0), naturalHeight - height);
    return { x: Math.round(x), y: Math.round(y), width: Math.round(width), height: Math.round(height) };
}

canvas.addEventListener('pointerdown', event => {
    if (!crop) return;
    const point = bufferPointFromEvent(event);
    const mode = hitTest(point);
    if (!mode) return;
    drag = { mode, anchor: { ...crop }, startX: point.x / canvasScale, startY: point.y / canvasScale };
    setActiveRatio('0'); // a free drag no longer matches whatever preset ratio was selected
    canvas.setPointerCapture(event.pointerId);
    event.preventDefault();
});

canvas.addEventListener('pointermove', event => {
    if (!crop) return;
    const point = bufferPointFromEvent(event);
    if (!drag) {
        canvas.style.cursor = CURSOR_BY_MODE[hitTest(point)] ?? 'default';
        return;
    }
    const x = point.x / canvasScale;
    const y = point.y / canvasScale;
    const dx = x - drag.startX;
    const dy = y - drag.startY;
    const a = drag.anchor;

    if (drag.mode === 'move') {
        setCrop(clampCrop({ x: a.x + dx, y: a.y + dy, width: a.width, height: a.height }));
    } else {
        // Each corner drags its own edges while the opposite edges stay anchored in place.
        const left = drag.mode.includes('w') ? a.x + dx : a.x;
        const top = drag.mode.includes('n') ? a.y + dy : a.y;
        const right = drag.mode.includes('e') ? a.x + a.width + dx : a.x + a.width;
        const bottom = drag.mode.includes('s') ? a.y + a.height + dy : a.y + a.height;
        setCrop(clampCrop({ x: Math.min(left, right), y: Math.min(top, bottom), width: Math.abs(right - left), height: Math.abs(bottom - top) }));
    }
});

canvas.addEventListener('pointerup', event => {
    drag = null;
    canvas.releasePointerCapture?.(event.pointerId);
});

resetCropButton.addEventListener('click', resetCrop);
for (const button of ratioButtons) {
    button.addEventListener('click', () => applyRatio(button.dataset.ratio));
}
for (const input of [cropXInput, cropYInput, cropWInput, cropHInput]) {
    input.addEventListener('change', () => { if (crop) applyManualCrop(); });
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
        download.textContent = `Download ${pending.name} (${formatBytes(result.length)})`;
        download.hidden = false;

        resultTable.hidden = false;
        if (RENDERABLE_EXTENSIONS.has(extensionOf(pending.name))) {
            previewAfter.src = downloadUrl;
            previewAfter.hidden = false;
            afterPlaceholder.hidden = true;
        } else {
            previewAfter.hidden = true;
            afterPlaceholder.hidden = false;
            afterPlaceholder.textContent = 'Preview not available for this format';
        }
        afterMeta.textContent = formatBytes(result.length);

        const ratio = pending.size > 0 ? `${(result.length / pending.size * 100).toFixed(1)}%` : 'n/a';
        const applied = Object.entries(pending.options).map(([key, value]) => `${key}=${value}`).join(', ');
        const extras = [];
        if (pending.cropWidth > 0) extras.push(`cropped to ${pending.cropWidth}×${pending.cropHeight} at (${pending.cropX}, ${pending.cropY})`);
        if (pending.resizeWidth > 0 && pending.resizeHeight > 0) extras.push(`resized to ${pending.resizeWidth}×${pending.resizeHeight}`);
        if (pending.targetSizeBytes > 0) extras.push(`target size ${formatBytes(pending.targetSizeBytes)}`);
        log.textContent = `${pending.sourceName} -> ${pending.name}\n` +
            `Input: ${pending.size.toLocaleString()} bytes · Output: ${result.length.toLocaleString()} bytes\n` +
            `Output / input: ${ratio} · Conversion: ${Math.round(data.elapsedMs).toLocaleString()} ms` +
            (extras.length ? `\n${extras.join(' · ')}` : '') +
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
        message.textContent = 'This encoder has no adjustable options.';
        optionsFields.append(message);
    }
    for (const option of format.parameters) {
        const row = document.createElement('p');
        const label = document.createElement('label');
        const id = `option-${option.id}`;
        label.htmlFor = id;
        label.textContent = option.label;
        const help = document.createElement('small');
        help.id = `${id}-help`;
        help.textContent = option.description;
        let control;
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
        } else if (option.kind === 'Integer') {
            control = document.createElement('input');
            control.type = 'number';
            control.step = '1';
            control.required = true;
            if (option.minimum !== null) control.min = String(option.minimum);
            if (option.maximum !== null) control.max = String(option.maximum);
            control.value = values[option.id];
        } else {
            throw new Error(`Unsupported option type: ${option.kind}`);
        }
        control.id = id;
        control.setAttribute('aria-describedby', help.id);
        control.addEventListener('input', () => {
            values[option.id] = option.kind === 'Boolean' ? String(control.checked) : control.value;
            updateVisibility();
        });
        row.append(label, document.createElement('br'), control, document.createElement('br'), help);
        optionsFields.append(row);
        rows.push({ row, option });
    }
    updateVisibility();
}

targetSelect.addEventListener('change', () => renderOptions());
resetButton.addEventListener('click', () => renderOptions(true));
sourceInput.addEventListener('change', () => {
    clearResult();
    const file = sourceInput.files?.[0];
    if (file) {
        loadPreview(file);
    } else {
        editor.hidden = true;
        previewBitmap?.close();
        previewBitmap = null;
    }
});

for (const eventName of ['dragenter', 'dragover']) {
    dropzone.addEventListener(eventName, event => {
        event.preventDefault();
        if (!sourceInput.disabled) dropzone.classList.add('drag');
    });
}
for (const eventName of ['dragleave', 'dragend']) {
    dropzone.addEventListener(eventName, () => dropzone.classList.remove('drag'));
}
dropzone.addEventListener('drop', event => {
    event.preventDefault();
    dropzone.classList.remove('drag');
    if (!ready || sourceInput.disabled || !event.dataTransfer.files.length) return;
    sourceInput.files = event.dataTransfer.files;
    sourceInput.dispatchEvent(new Event('change'));
});

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
    const resizeWidth = resizeWidthInput.value ? Number(resizeWidthInput.value) : 0;
    const resizeHeight = resizeHeightInput.value ? Number(resizeHeightInput.value) : 0;
    if ((resizeWidth > 0) !== (resizeHeight > 0)) {
        log.textContent = 'Set both resize width and height, or leave both blank.';
        return;
    }
    let targetSizeBytes = 0;
    try {
        targetSizeBytes = parseTargetSize(targetSizeInput.value);
    } catch (error) {
        log.textContent = String(error.message ?? error);
        return;
    }
    const cropped = crop && (crop.x !== 0 || crop.y !== 0 || crop.width !== naturalWidth || crop.height !== naturalHeight);
    pending = {
        name: file.name.slice(0, dot) + format.extension,
        sourceName: file.name,
        size: file.size,
        options: Object.fromEntries(format.parameters
            .filter(option => !option.condition || settings.get(format.id)[option.condition.id] === option.condition.value)
            .map(option => [option.id, settings.get(format.id)[option.id]])),
        cropX: cropped ? crop.x : 0, cropY: cropped ? crop.y : 0,
        cropWidth: cropped ? crop.width : 0, cropHeight: cropped ? crop.height : 0,
        resizeWidth, resizeHeight, targetSizeBytes,
    };
    clearResult();
    setBusy(true);
    log.textContent = 'Converting with the selected encoder settings…';
    try {
        const bytes = await file.arrayBuffer();
        worker.postMessage({ type: 'convert', bytes, sourceExtension: file.name.slice(dot),
            targetExtension: format.extension, options: pending.options,
            cropX: pending.cropX, cropY: pending.cropY, cropWidth: pending.cropWidth, cropHeight: pending.cropHeight,
            resizeWidth, resizeHeight, targetSizeBytes }, [bytes]);
    } catch (error) {
        log.textContent = String(error);
        pending = null;
        setBusy(false);
    }
});
window.addEventListener('pagehide', () => {
    clearResult();
    previewBitmap?.close();
});
