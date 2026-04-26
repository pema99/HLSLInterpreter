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

        if (target === 'debug') {
            if (state.debugPixel && typeof window.dbgSetDebugPixel === 'function')
                window.dbgSetDebugPixel(id, state.debugPixel.x, state.debugPixel.y);
            if (state.threadStates && typeof window.dbgSetViewportThreadStates === 'function')
                window.dbgSetViewportThreadStates(id, state.threadStates);
        }

        if (typeof window.dbgSetClickHandler === 'function') {
            if (target === 'regular' && mode === 'gpu') {
                window.dbgSetClickHandler(id, (px, py) => {
                    const snap = window.gpuSnapshot?.();
                    if (!snap || !window._dotNetDebugRef) return;
                    window.gpuStop?.();
                    window._dotNetDebugRef.invokeMethodAsync('StartDebugAtPixel', px, py, snap[0], snap[1], snap[2]);
                });
            } else if (target === 'regular' && state.cpuClickWarp) {
                const [wx, wy] = state.cpuClickWarp;
                window.dbgSetClickHandler(id, (px, py) => {
                    if (!window._dotNetDebugRef) return;
                    window._dotNetDebugRef.invokeMethodAsync('StartDebugAtPixel', px, py, 0, wx, wy);
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
        state.pixels = pixels;
        state.width = width;
        state.height = height;
        applyAll();
        document.querySelectorAll('.image-container[data-mode-target]').forEach(c => {
            if (typeof window.dbgResetView === 'function') window.dbgResetView(c.id);
        });
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
})();
