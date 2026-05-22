// Host helpers, stuff only JS can do

let dotNetDebugRef = null;

export function setDebuggerRef(ref) { dotNetDebugRef = ref; }
export function getDebuggerRef() { return dotNetDebugRef; }

export async function dbgFetchText(url) {
    const r = await fetch(url);
    if (!r.ok) throw new Error('fetch ' + url + ' -> ' + r.status);
    return await r.text();
}

export function dbgPickObj() {
    const input = document.createElement('input');
    input.type = 'file';
    input.accept = '.obj';
    input.addEventListener('change', () => {
        const file = input.files && input.files[0];
        if (!file) return;
        const reader = new FileReader();
        reader.onload = ev => {
            if (dotNetDebugRef)
                dotNetDebugRef.invokeMethodAsync('LoadObjMesh', ev.target.result);
        };
        reader.readAsText(file);
    });
    input.click();
}

export function copyToClipboard(text) {
    return navigator.clipboard.writeText(text);
}

export function scrollImmediateToBottom() {
    requestAnimationFrame(function () {
        var el = document.querySelector('.imm-body');
        if (el) el.scrollTop = el.scrollHeight;
    });
}

// --- Image rendering ---

// Paints a per-thread rgba grid onto `canvas`, upscaled with no smoothing, with
// a black-red-black ring around the inspected thread. sizeScale shrinks both the
// output size and the per-cell minimum together so the ring stays crisp.
function paintScaledRgbaToCanvas(canvas, rgbaBytes, width, height, inspectedX, inspectedY, sizeScale) {
    var s = sizeScale || 1;
    var bytes = rgbaBytes;
    // Blazor delivers byte[] as a base64 string over JS interop.
    if (typeof bytes === 'string') {
        var bin = atob(bytes);
        var arr = new Uint8ClampedArray(bin.length);
        for (var i = 0; i < bin.length; i++) arr[i] = bin.charCodeAt(i);
        bytes = arr;
    }
    var src = document.createElement('canvas');
    src.width = width;
    src.height = height;
    src.getContext('2d').putImageData(
        new ImageData(new Uint8ClampedArray(bytes), width, height), 0, 0);
    var pxScale = Math.max(Math.round(8 * s), Math.floor(192 * s / Math.max(width, height)));
    canvas.width = width * pxScale;
    canvas.height = height * pxScale;
    var dctx = canvas.getContext('2d');
    dctx.imageSmoothingEnabled = false;
    dctx.drawImage(src, 0, 0, canvas.width, canvas.height);
    var ix = inspectedX * pxScale;
    var iy = inspectedY * pxScale;
    var ringDraw = function (x, y, w, h, color) {
        dctx.fillStyle = color;
        dctx.fillRect(x, y, w, 1);
        dctx.fillRect(x, y + h - 1, w, 1);
        dctx.fillRect(x, y, 1, h);
        dctx.fillRect(x + w - 1, y, 1, h);
    };
    ringDraw(ix - 1, iy - 1, pxScale + 2, pxScale + 2, '#000');
    ringDraw(ix - 2, iy - 2, pxScale + 4, pxScale + 4, '#f00');
    ringDraw(ix - 3, iy - 3, pxScale + 6, pxScale + 6, '#000');
}

export function rgbaToDataUrl(rgbaBytes, width, height, inspectedX, inspectedY, sizeScale) {
    var dst = document.createElement('canvas');
    paintScaledRgbaToCanvas(dst, rgbaBytes, width, height, inspectedX, inspectedY, sizeScale);
    return dst.toDataURL('image/png');
}

async function decodeImageBlob(fileName, blob) {
    const bitmap = await createImageBitmap(blob);
    const w = bitmap.width;
    const h = bitmap.height;
    const canvas = document.createElement('canvas');
    canvas.width = w;
    canvas.height = h;
    const ctx = canvas.getContext('2d');
    ctx.drawImage(bitmap, 0, 0);
    const data = ctx.getImageData(0, 0, w, h);
    const previewBlob = await new Promise(r => canvas.toBlob(r, 'image/png'));
    const blobUrl = URL.createObjectURL(previewBlob);
    bitmap.close && bitmap.close();
    let bin = '';
    const u8 = data.data;
    const CHUNK = 0x8000;
    for (let i = 0; i < u8.length; i += CHUNK) {
        bin += String.fromCharCode.apply(null, u8.subarray(i, i + CHUNK));
    }
    return {
        fileName: fileName,
        width: w,
        height: h,
        rgba8Base64: btoa(bin),
        dataUrl: blobUrl,
    };
}

export function dbgPickImage() {
    return new Promise((resolve, reject) => {
        const input = document.createElement('input');
        input.type = 'file';
        input.accept = 'image/*';
        input.addEventListener('change', async () => {
            const file = input.files && input.files[0];
            if (!file) { resolve(null); return; }
            try { resolve(await decodeImageBlob(file.name, file)); }
            catch (e) { reject(e); }
        });
        input.click();
    });
}

export function dbgRevokeBlobUrl(url) {
    if (url && typeof url === 'string' && url.startsWith('blob:')) URL.revokeObjectURL(url);
}

export async function dbgFetchImage(url) {
    const response = await fetch(url);
    if (!response.ok) throw new Error('HTTP ' + response.status + ' fetching ' + url);
    const blob = await response.blob();
    const fileName = (() => { try { return new URL(url, document.baseURI).pathname.split('/').pop() || url; } catch (_) { return url; } })();
    return await decodeImageBlob(fileName, blob);
}

export function downloadTextFile(filename, content) {
    const blob = new Blob([content], { type: 'text/plain;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
}

// --- glsl2hlsl transpiler ---

let _glsl2hlslPromise = null;
function loadGlsl2Hlsl() {
    if (!_glsl2hlslPromise) {
        _glsl2hlslPromise = (async () => {
            const base = new URL('_content/HLSLInterpreter.Debugger/lib/glsl2hlsl/', document.baseURI).href;
            const mod = await import(base + 'glsl2hlsl_wasm.js');
            await mod.default(base + 'glsl2hlsl_wasm_bg.wasm');
            return mod;
        })();
    }
    return _glsl2hlslPromise;
}

export async function glsl2hlslTranspile(glsl) {
    const mod = await loadGlsl2Hlsl();
    return mod.transpile(glsl);
}

// --- Thread grid layout ---

let _threadGridResizeObserver = null;

function fitThreadGrid(containerId, cols, rows) {
    const container = document.getElementById(containerId);
    if (!container) return;
    const grid = container.querySelector('.thread-grid');
    if (!grid) return;
    const cw = container.clientWidth - 12;  // account for padding
    const ch = container.clientHeight - 12;
    if (cw <= 0 || ch <= 0 || cols <= 0 || rows <= 0) return;
    // Account for gaps so cells do not overflow. Try gap=2, fall back to 1 for
    // small cells.
    let gap = 2;
    let cellSize = Math.max(2, Math.floor(Math.min(
        (cw - (cols - 1) * gap) / cols,
        (ch - (rows - 1) * gap) / rows
    )));
    if (cellSize <= 4) {
        gap = 1;
        cellSize = Math.max(2, Math.floor(Math.min(
            (cw - (cols - 1) * gap) / cols,
            (ch - (rows - 1) * gap) / rows
        )));
    }
    grid.style.gridTemplateColumns = `repeat(${cols}, ${cellSize}px)`;
    grid.style.gridTemplateRows = `repeat(${rows}, ${cellSize}px)`;
    grid.style.gap = gap + 'px';
}

export function initThreadGridResize(containerId, cols, rows) {
    disposeThreadGridResize();
    const container = document.getElementById(containerId);
    if (!container) return;
    fitThreadGrid(containerId, cols, rows);
    _threadGridResizeObserver = new ResizeObserver(function () {
        fitThreadGrid(containerId, cols, rows);
    });
    _threadGridResizeObserver.observe(container);
}

export function disposeThreadGridResize() {
    if (_threadGridResizeObserver) {
        _threadGridResizeObserver.disconnect();
        _threadGridResizeObserver = null;
    }
}

// --- Panel resize handles ---

// Inner horizontal resize of call stack / execution state divider.
(function () {
    var dragging = false;
    var startX, startW, pane;

    document.addEventListener('mousedown', function (e) {
        if (!e.target.classList.contains('resize-inner-h-handle')) return;
        dragging = true;
        startX = e.clientX;
        var body = e.target.closest('.callstack-exec-body');
        pane = body ? body.querySelector('.callstack-pane') : null;
        if (!pane) { dragging = false; return; }
        startW = pane.getBoundingClientRect().width;
        e.target.classList.add('dragging');
        e.preventDefault();
    });

    document.addEventListener('mousemove', function (e) {
        if (!dragging || !pane) return;
        var body = pane.closest('.callstack-exec-body');
        var totalW = body ? body.getBoundingClientRect().width : 0;
        var newW = Math.max(40, Math.min(totalW - 44, startW + (e.clientX - startX)));
        pane.style.flex = '0 0 ' + newW + 'px';
    });

    document.addEventListener('mouseup', function () {
        if (!dragging) return;
        dragging = false;
        var handle = document.getElementById('callstack-exec-divider');
        if (handle) handle.classList.remove('dragging');
        pane = null;
    });
})();

// Outer resize of the horizontal panel divider and the vertical section dividers.
(function () {
    let activeHandle = null;  // null | 'horizontal' | 'horizontal-narrow' | { type: 'vertical', section }
    let startPos, startSize;

    document.addEventListener('mousedown', function (e) {
        if (e.target.classList.contains('resize-h-handle')) {
            const panel = document.querySelector('.output-panel');
            if (window.innerWidth <= 768) {
                activeHandle = 'horizontal-narrow';
                startPos = e.clientY;
                startSize = panel.getBoundingClientRect().height;
            } else {
                activeHandle = 'horizontal';
                startPos = e.clientX;
                startSize = panel.getBoundingClientRect().width;
            }
            e.preventDefault();
        } else if (e.target.classList.contains('resize-v-handle')) {
            const section = e.target.closest('.image-section, .debug-section-image, .debug-section-console, .debug-section-vars, .debug-section-callstack, .debug-section-immediate');
            if (section) {
                activeHandle = { type: 'vertical', section };
                startPos = e.clientY;
                startSize = section.getBoundingClientRect().height;
                e.preventDefault();
            }
        }
    });

    document.addEventListener('mousemove', function (e) {
        if (!activeHandle) return;
        if (activeHandle === 'horizontal') {
            const newWidth = Math.max(160, startSize - (e.clientX - startPos));
            document.querySelector('.output-panel').style.width = newWidth + 'px';
        } else if (activeHandle === 'horizontal-narrow') {
            const newHeight = Math.max(120, startSize - (e.clientY - startPos));
            document.querySelector('.output-panel').style.height = newHeight + 'px';
        } else if (activeHandle.type === 'vertical') {
            const newHeight = Math.max(60, Math.min(800, startSize + (e.clientY - startPos)));
            activeHandle.section.style.setProperty('--section-h', newHeight + 'px');
        }
        e.preventDefault();
    });

    document.addEventListener('mouseup', function () {
        activeHandle = null;
    });
})();
