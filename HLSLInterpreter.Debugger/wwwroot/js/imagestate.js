// Cached state for the Color Output's painted image, viewport mode, and click
// handlers. C# pushes updates via the imgSet* setters. A MutationObserver
// watches for .image-container[data-mode-target] elements appearing and
// applies the cached state to each one, so it doesn't matter whether a
// container exists when C# pushes its state.
(function () {
    const state = {
        pixels: null, width: 0, height: 0,
        warpX: 1, warpY: 1,
        isGpuMode: false,
        regularMode: 'cpu',
        debugMode: 'idle',
        debugPixel: null,
        threadStates: null,
        cpuClickWarp: null,
        debugClickActive: false,
        pickMode: 'pixel',  // 'pixel' or 'vertex'
        meshPositions: null,
        meshIndices: null,
    };

    function paint(canvas) {
        if (!canvas || !state.pixels || !state.width || !state.height) return;
        canvas.width = state.width;
        canvas.height = state.height;
        canvas.getContext('2d').putImageData(
            new ImageData(new Uint8ClampedArray(state.pixels), state.width, state.height), 0, 0);
    }

    function applyTo(container) {
        if (!container || !container.dataset || !container.dataset.modeTarget) return;
        const target = container.dataset.modeTarget;
        const id = container.id;

        paint(container.querySelector('.color-canvas-2d'));

        if (typeof window.dbgInitViewport === 'function')
            window.dbgInitViewport(id);

        const mode = target === 'regular' ? state.regularMode : state.debugMode;

        // In GPU mode, gpu.js pushes the canvas pixel size itself.
        if (mode !== 'gpu' && typeof window.dbgSetViewportImageSize === 'function')
            window.dbgSetViewportImageSize(id, state.width || 1, state.height || 1);

        if (typeof window.dbgSetViewportWarp === 'function')
            window.dbgSetViewportWarp(id, state.warpX, state.warpY);

        if (typeof window.dbgSetViewportMode === 'function')
            window.dbgSetViewportMode(id, mode);

        if (typeof window.dbgSetViewportPickMode === 'function')
            window.dbgSetViewportPickMode(id, state.pickMode, state.meshPositions, state.meshIndices);

        if (target === 'debug') {
            if (state.debugPixel && typeof window.dbgSetDebugPixel === 'function')
                window.dbgSetDebugPixel(id, state.debugPixel.x, state.debugPixel.y);
            if (state.threadStates && typeof window.dbgSetViewportThreadStates === 'function')
                window.dbgSetViewportThreadStates(id, state.threadStates);
        }

        if (typeof window.dbgSetClickHandler === 'function') {
            if (target === 'regular' && state.pickMode === 'vertex') {
                window.dbgSetClickHandler(id, (px, py, vertexIndex) => {
                    if (vertexIndex == null || !window._dotNetDebugRef) return;
                    if (mode === 'gpu') {
                        const snap = window.gpuSnapshot?.();
                        if (!snap) return;
                        window.gpuPause?.();
                        window._dotNetDebugRef.invokeMethodAsync('StartDebugAtVertex',
                            vertexIndex, snap[0], snap[1], snap[2]);
                    } else {
                        window._dotNetDebugRef.invokeMethodAsync('StartDebugAtVertex',
                            vertexIndex, 0, state.width || 1, state.height || 1);
                    }
                });
            } else if (target === 'regular' && mode === 'gpu') {
                window.dbgSetClickHandler(id, (px, py) => {
                    const snap = window.gpuSnapshot?.();
                    if (!snap || !window._dotNetDebugRef) return;
                    window.gpuPause?.();
                    window._dotNetDebugRef.invokeMethodAsync('StartDebugAtPixel',
                        px, py, snap[0], snap[1], snap[2]);
                });
            } else if (target === 'regular' && state.cpuClickWarp) {
                const [wx, wy] = state.cpuClickWarp;
                window.dbgSetClickHandler(id, (px, py) => {
                    if (!window._dotNetDebugRef) return;
                    window._dotNetDebugRef.invokeMethodAsync('StartDebugAtPixel',
                        px, py, 0, state.width || wx, state.height || wy);
                });
            } else if (target === 'debug' && state.debugClickActive) {
                window.dbgSetClickHandler(id, (px, py) => {
                    if (!window._dotNetDebugRef) return;
                    window._dotNetDebugRef.invokeMethodAsync('SetInspectedPixel', px, py);
                });
            } else {
                window.dbgSetClickHandler(id, null);
            }
        }
    }

    function applyAll() {
        document.querySelectorAll('.image-container[data-mode-target]').forEach(applyTo);
    }

    new MutationObserver(records => {
        for (const r of records) {
            for (const node of r.addedNodes) {
                if (node.nodeType !== 1) continue;
                if (node.matches?.('.image-container[data-mode-target]')) applyTo(node);
                node.querySelectorAll?.('.image-container[data-mode-target]').forEach(applyTo);
            }
        }
    }).observe(document.body, { childList: true, subtree: true });

    window.imgSetPixels = function (pixels, width, height) {
        // Wrap into a writable typed array so imgSetPixelsRect can mutate in place.
        if (pixels && !(pixels instanceof Uint8ClampedArray)) {
            pixels = new Uint8ClampedArray(pixels);
        }
        state.pixels = pixels;
        state.width = width;
        state.height = height;
        applyAll();
        document.querySelectorAll('.image-container[data-mode-target]').forEach(c => {
            if (typeof window.dbgResetView === 'function') window.dbgResetView(c.id);
        });
    };

    window.imgSetPixelsRect = function (pixels, x, y, rectW, rectH) {
        if (!state.pixels || state.width <= 0 || state.height <= 0) return;
        const fullW = state.width;
        const fullH = state.height;
        // Alias the incoming buffer without copying so putImageData can read it directly.
        let src;
        if (pixels instanceof Uint8ClampedArray) src = pixels;
        else if (ArrayBuffer.isView(pixels)) src = new Uint8ClampedArray(pixels.buffer, pixels.byteOffset, pixels.byteLength);
        else src = new Uint8ClampedArray(pixels);
        for (let row = 0; row < rectH; row++) {
            const dstY = y + row;
            if (dstY < 0 || dstY >= fullH) continue;
            const copyW = Math.min(rectW, fullW - x);
            if (copyW <= 0) continue;
            const srcStart = row * rectW * 4;
            const dstStart = (dstY * fullW + x) * 4;
            state.pixels.set(src.subarray(srcStart, srcStart + copyW * 4), dstStart);
        }
        document.querySelectorAll('.image-container[data-mode-target] .color-canvas-2d').forEach(canvas => {
            if (!canvas || canvas.width !== fullW || canvas.height !== fullH) return;
            canvas.getContext('2d').putImageData(new ImageData(src, rectW, rectH), x, y);
        });
    };

    window.imgAllocPixels = function (width, height) {
        const pixels = new Uint8ClampedArray(width * height * 4);
        for (let i = 3; i < pixels.length; i += 4) pixels[i] = 255;
        window.imgSetPixels(pixels, width, height);
    };

    window.cpuCanvasSize = function () {
        const el = document.getElementById('image-container');
        if (!el) return [256, 256];
        const dpr = window.devicePixelRatio || 1;
        const cw = Math.max(1, Math.floor(el.clientWidth * dpr));
        const ch = Math.max(1, Math.floor(el.clientHeight * dpr));
        return [cw, ch];
    };

    window.imgSetWarp = function (warpX, warpY) {
        state.warpX = warpX;
        state.warpY = warpY;
        applyAll();
    };

    window.imgSetRegularMode = function (mode) {
        state.regularMode = mode;
        applyAll();
    };

    window.imgSetDebugMode = function (mode) {
        state.debugMode = mode;
        applyAll();
    };

    window.imgSetDebugPixel = function (px, py) {
        state.debugPixel = (px == null || py == null) ? null : { x: px, y: py };
        applyAll();
    };

    window.imgSetThreadStates = function (states) {
        state.threadStates = states;
        applyAll();
    };

    window.imgSetCpuClickHandler = function (warpX, warpY) {
        state.cpuClickWarp = (warpX == null) ? null : [warpX, warpY];
        applyAll();
    };

    window.imgSetDebugClickHandler = function (active) {
        state.debugClickActive = !!active;
        applyAll();
    };

    window.imgSetPickMode = function (mode) {
        state.pickMode = (mode === 'vertex') ? 'vertex' : 'pixel';
        applyAll();
    };

    window.imgSetMeshData = function (positions, indices) {
        state.meshPositions = positions;
        state.meshIndices = indices;
        applyAll();
    };
})();
