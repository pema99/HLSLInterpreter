// Painting and overlay setup for the Color Output canvas. JS keeps no copy of
// the image: C# owns it and CanvasView pushes it (on mount, or when a new image
// is produced). The functions here just put pixels on the <canvas>.
import {
    dbgInitViewport, dbgSetViewportImageSize, dbgSetViewportWarp, dbgSetViewportMode,
    dbgSetViewportPickMode, dbgSetDebugPixel, dbgSetViewportThreadStates,
    dbgSetClickHandler, dbgResetView,
} from './viewport.js';
import { gpuSnapshot, gpuPause } from './gpu.js';
import { getDebuggerRef } from './host.js';

// The picking mesh is the one thing retained here: it is needed by every
// applyCanvasState call and changes only rarely.
let _meshPositions = null;
let _meshIndices = null;

function the2dCanvas() {
    return document.querySelector('.image-container .color-canvas-2d');
}

// Apply the overlay projection to one container. Called by CanvasView on render.
export function applyCanvasState(containerId, p) {
    const container = document.getElementById(containerId);
    if (!container) return;
    dbgInitViewport(containerId);

    const target = container.dataset.modeTarget;
    const mode = target === 'regular' ? p.regularMode : p.debugMode;

    dbgSetViewportWarp(containerId, p.warpX, p.warpY);
    dbgSetViewportMode(containerId, mode);
    dbgSetViewportPickMode(containerId, p.pickMode, _meshPositions, _meshIndices);

    if (target === 'debug') {
        if (p.debugPixel) dbgSetDebugPixel(containerId, p.debugPixel.x, p.debugPixel.y);
        if (p.threadStates) dbgSetViewportThreadStates(containerId, p.threadStates);
    }

    if (target === 'regular' && p.pickMode === 'vertex') {
        dbgSetClickHandler(containerId, (px, py, vertexIndex) => {
            const ref = getDebuggerRef();
            if (vertexIndex == null || !ref) return;
            if (mode === 'gpu') {
                const snap = gpuSnapshot();
                if (!snap) return;
                gpuPause();
                ref.invokeMethodAsync('StartDebugAtVertex', vertexIndex, snap[0], snap[1], snap[2]);
            } else {
                const c = the2dCanvas();
                ref.invokeMethodAsync('StartDebugAtVertex', vertexIndex, 0,
                    c ? c.width : 1, c ? c.height : 1);
            }
        });
    } else if (target === 'regular' && mode === 'gpu') {
        dbgSetClickHandler(containerId, (px, py) => {
            const snap = gpuSnapshot();
            const ref = getDebuggerRef();
            if (!snap || !ref) return;
            gpuPause();
            ref.invokeMethodAsync('StartDebugAtPixel', px, py, snap[0], snap[1], snap[2]);
        });
    } else if (target === 'regular' && p.cpuClick) {
        dbgSetClickHandler(containerId, (px, py) => {
            const ref = getDebuggerRef();
            if (!ref) return;
            const c = the2dCanvas();
            ref.invokeMethodAsync('StartDebugAtPixel', px, py, 0,
                c ? c.width : p.cpuClick.x, c ? c.height : p.cpuClick.y);
        });
    } else if (target === 'debug' && p.debugClick) {
        dbgSetClickHandler(containerId, (px, py) => {
            const ref = getDebuggerRef();
            if (ref) ref.invokeMethodAsync('SetInspectedPixel', px, py);
        });
    } else {
        dbgSetClickHandler(containerId, null);
    }
}

export function setPixels(pixels, width, height) {
    const container = document.querySelector('.image-container');
    if (!container) return;
    const canvas = container.querySelector('.color-canvas-2d');
    if (canvas) {
        const src = pixels instanceof Uint8ClampedArray ? pixels : new Uint8ClampedArray(pixels);
        canvas.width = width;
        canvas.height = height;
        canvas.getContext('2d').putImageData(new ImageData(src, width, height), 0, 0);
    }
    dbgSetViewportImageSize(container.id, width, height);
    dbgResetView(container.id);
}

export function allocPixels(width, height) {
    const container = document.querySelector('.image-container');
    if (!container) return;
    const canvas = container.querySelector('.color-canvas-2d');
    if (canvas) {
        canvas.width = width;
        canvas.height = height;
        const ctx = canvas.getContext('2d');
        ctx.fillStyle = '#000';
        ctx.fillRect(0, 0, width, height);
    }
    dbgSetViewportImageSize(container.id, width, height);
    dbgResetView(container.id);
}

// Paint one tile straight onto the canvas; the canvas accumulates the frame.
export function setPixelsRect(pixels, x, y, rectW, rectH) {
    const canvas = the2dCanvas();
    if (!canvas) return;
    const src = pixels instanceof Uint8ClampedArray ? pixels
        : ArrayBuffer.isView(pixels) ? new Uint8ClampedArray(pixels.buffer, pixels.byteOffset, pixels.byteLength)
        : new Uint8ClampedArray(pixels);
    canvas.getContext('2d').putImageData(new ImageData(src, rectW, rectH), x, y);
}

export function setMeshData(positions, indices) {
    _meshPositions = positions;
    _meshIndices = indices;
}

export function cpuCanvasSize() {
    const el = document.querySelector('.image-container');
    if (!el) return [256, 256];
    const dpr = window.devicePixelRatio || 1;
    const cw = Math.max(1, Math.floor(el.clientWidth * dpr));
    const ch = Math.max(1, Math.floor(el.clientHeight * dpr));
    return [cw, ch];
}
