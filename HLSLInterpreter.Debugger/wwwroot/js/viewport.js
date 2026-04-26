// Zoom/pan + overlay rectangles for the Color Output panel. The image canvas
// is transformed in CSS for zoom/pan. The overlay canvas is drawn in device
// pixels so its rings stay crisp at any DPR.
(function () {
    const states = new WeakMap();

    function getState(container) {
        let s = states.get(container);
        if (!s) {
            s = {
                tx: 0, ty: 0, scale: 1,
                imgW: 1, imgH: 1,
                warpX: 1, warpY: 1,
                box: { x: 0, y: 0, w: 1, h: 1 },
                mode: 'idle',
                debugPixel: null,
                hoverPixel: null,
                onClick: null,
                dragging: false,
                resizeObserver: null,
            };
            states.set(container, s);
        }
        return s;
    }

    // GPU mode fills the container. The other modes preserve aspect so the
    // tiny warp image doesn't render with wide rectangular pixels.
    function fitImageBox(container, s) {
        const cw = container.clientWidth;
        const ch = container.clientHeight;
        const aspectFit = s.mode === 'debug' || s.mode === 'cpu' || s.mode === 'both';
        if (!aspectFit || cw <= 0 || ch <= 0 || s.imgW <= 0 || s.imgH <= 0) {
            return { x: 0, y: 0, w: cw, h: ch };
        }
        const ar = s.imgW / s.imgH;
        const conAR = cw / ch;
        let w, h, x, y;
        if (conAR > ar) {
            h = ch; w = ch * ar;
            x = (cw - w) / 2; y = 0;
        } else {
            w = cw; h = cw / ar;
            x = 0; y = (ch - h) / 2;
        }
        return { x, y, w, h };
    }

    function clampPan(s) {
        const minTx = s.box.w * (1 - s.scale);
        const minTy = s.box.h * (1 - s.scale);
        if (s.tx > 0) s.tx = 0;
        if (s.tx < minTx) s.tx = minTx;
        if (s.ty > 0) s.ty = 0;
        if (s.ty < minTy) s.ty = minTy;
    }

    function applyLayout(container, s) {
        clampPan(s);
        const t = `translate(${s.tx}px, ${s.ty}px) scale(${s.scale})`;
        container.querySelectorAll('.image-canvas').forEach(el => {
            el.style.left = s.box.x + 'px';
            el.style.top = s.box.y + 'px';
            el.style.width = s.box.w + 'px';
            el.style.height = s.box.h + 'px';
            el.style.transform = t;
        });
        refreshOverlay(container, s);
    }

    function clientToImagePx(container, s, clientX, clientY) {
        const rect = container.getBoundingClientRect();
        const localX = clientX - rect.left - s.box.x - s.tx;
        const localY = clientY - rect.top - s.box.y - s.ty;
        const sx = (s.box.w / s.imgW) * s.scale;
        const sy = (s.box.h / s.imgH) * s.scale;
        if (sx <= 0 || sy <= 0) return null;
        return { x: localX / sx, y: localY / sy };
    }

    function drawRing(ctx, x, y, w, h, color) {
        if (w <= 0 || h <= 0) return;
        ctx.fillStyle = color;
        ctx.fillRect(x, y, w, 1);
        ctx.fillRect(x, y + h - 1, w, 1);
        ctx.fillRect(x, y, 1, h);
        ctx.fillRect(x + w - 1, y, 1, h);
    }

    function drawX(ctx, dpr, s, px, py, color) {
        const sx = (s.box.w / s.imgW) * s.scale;
        const sy = (s.box.h / s.imgH) * s.scale;
        const left = Math.round((s.box.x + s.tx + px * sx) * dpr);
        const top = Math.round((s.box.y + s.ty + py * sy) * dpr);
        const right = Math.round((s.box.x + s.tx + (px + 1) * sx) * dpr);
        const bottom = Math.round((s.box.y + s.ty + (py + 1) * sy) * dpr);
        // Black-red-black 1-1-1 sandwich: 3px black, then 1px colored on top.
        const drawDiagonals = (width, col) => {
            ctx.strokeStyle = col;
            ctx.lineWidth = width;
            ctx.lineCap = 'butt';
            ctx.beginPath();
            ctx.moveTo(left + 2, top + 2);
            ctx.lineTo(right - 2, bottom - 2);
            ctx.moveTo(right - 2, top + 2);
            ctx.lineTo(left + 2, bottom - 2);
            ctx.stroke();
        };
        drawDiagonals(3, '#000');
        drawDiagonals(1, color);
    }

    function drawHighlight(ctx, dpr, s, px, py, w, h, middleColor) {
        const sx = (s.box.w / s.imgW) * s.scale;
        const sy = (s.box.h / s.imgH) * s.scale;
        const left = Math.round((s.box.x + s.tx + px * sx) * dpr);
        const top = Math.round((s.box.y + s.ty + py * sy) * dpr);
        const right = Math.round((s.box.x + s.tx + (px + w) * sx) * dpr);
        const bottom = Math.round((s.box.y + s.ty + (py + h) * sy) * dpr);
        const rectW = right - left;
        const rectH = bottom - top;
        drawRing(ctx, left - 1, top - 1, rectW + 2, rectH + 2, '#000');
        drawRing(ctx, left - 2, top - 2, rectW + 4, rectH + 4, middleColor);
        drawRing(ctx, left - 3, top - 3, rectW + 6, rectH + 6, '#000');
    }

    function ensureOverlayCanvas(container) {
        const canvas = container.querySelector('.image-overlay');
        if (!canvas) return null;
        const dpr = window.devicePixelRatio || 1;
        const cssW = container.clientWidth;
        const cssH = container.clientHeight;
        const tw = Math.max(1, Math.floor(cssW * dpr));
        const th = Math.max(1, Math.floor(cssH * dpr));
        if (canvas.width !== tw) canvas.width = tw;
        if (canvas.height !== th) canvas.height = th;
        canvas.style.width = cssW + 'px';
        canvas.style.height = cssH + 'px';
        return canvas;
    }

    function refreshOverlay(container, s) {
        const canvas = ensureOverlayCanvas(container);
        if (!canvas) return;
        const ctx = canvas.getContext('2d');
        ctx.imageSmoothingEnabled = false;
        ctx.clearRect(0, 0, canvas.width, canvas.height);
        const dpr = window.devicePixelRatio || 1;

        // Red X over each inactive thread (Both mode only).
        if (s.mode === 'both' && s.threadStates) {
            for (let i = 0; i < s.threadStates.length; i++) {
                if (s.threadStates[i] === 0) continue; // 0 = Active
                drawX(ctx, dpr, s, i % s.warpX, Math.floor(i / s.warpX), '#f00');
            }
        }
        // Persistent indicator for the currently inspected thread (debug + both).
        if ((s.mode === 'debug' || s.mode === 'both') && s.debugPixel) {
            drawHighlight(ctx, dpr, s, s.debugPixel.x, s.debugPixel.y, 1, 1, '#f00');
        }
        // Hover indicator: shown in any clickable mode.
        if ((s.mode === 'gpu' || s.mode === 'cpu' || s.mode === 'debug' || s.mode === 'both') && s.hoverPixel) {
            const px = Math.floor(s.hoverPixel.x);
            const py = Math.floor(s.hoverPixel.y);
            if (px >= 0 && py >= 0 && px < s.imgW && py < s.imgH) {
                if (s.mode === 'gpu') {
                    const wx = Math.floor(px / s.warpX) * s.warpX;
                    const wy = Math.floor(py / s.warpY) * s.warpY;
                    drawHighlight(ctx, dpr, s, wx, wy, s.warpX, s.warpY, '#fff');
                }
                drawHighlight(ctx, dpr, s, px, py, 1, 1, '#f00');
            }
        }
    }

    function onWheel(container, s, e) {
        e.preventDefault();
        const rect = container.getBoundingClientRect();
        const u = (e.clientX - rect.left) - s.box.x;
        const v = (e.clientY - rect.top) - s.box.y;
        const k = Math.exp(-e.deltaY * 0.0015);
        const newScale = Math.min(200, Math.max(1, s.scale * k));
        const factor = newScale / s.scale;
        s.tx = u - (u - s.tx) * factor;
        s.ty = v - (v - s.ty) * factor;
        s.scale = newScale;
        applyLayout(container, s);
    }

    function onMouseDown(container, s, e) {
        if (e.button !== 1) return;
        e.preventDefault();
        s.dragging = true;
        container.classList.add('panning');
    }

    function onMouseMove(container, s, e) {
        if (s.dragging) {
            s.tx += e.movementX;
            s.ty += e.movementY;
            applyLayout(container, s);
        }
        s.hoverPixel = clientToImagePx(container, s, e.clientX, e.clientY);
        refreshOverlay(container, s);
    }

    function onMouseUp(container, s, e) {
        if (s.dragging && e.button === 1) {
            s.dragging = false;
            container.classList.remove('panning');
        }
    }

    function onClick(container, s, e) {
        if (e.button !== 0 || !s.onClick) return;
        const pt = clientToImagePx(container, s, e.clientX, e.clientY);
        if (!pt) return;
        const px = Math.floor(pt.x);
        const py = Math.floor(pt.y);
        if (px < 0 || py < 0 || px >= s.imgW || py >= s.imgH) return;
        try { s.onClick(px, py); } catch (err) { console.error('[viewport] onClick threw', err); }
    }

    function onMouseLeave(container, s) {
        s.hoverPixel = null;
        refreshOverlay(container, s);
    }

    window.dbgInitViewport = function (containerId) {
        const container = document.getElementById(containerId);
        if (!container || container.__viewportInit) return;
        container.__viewportInit = true;
        const s = getState(container);

        container.addEventListener('wheel', e => onWheel(container, s, e), { passive: false });
        container.addEventListener('mousedown', e => onMouseDown(container, s, e));
        window.addEventListener('mousemove', e => {
            if (!container.isConnected) return;
            onMouseMove(container, s, e);
        });
        window.addEventListener('mouseup', e => {
            if (!container.isConnected) return;
            onMouseUp(container, s, e);
        });
        container.addEventListener('click', e => onClick(container, s, e));
        container.addEventListener('auxclick', e => { if (e.button === 1) e.preventDefault(); });
        container.addEventListener('mouseleave', () => onMouseLeave(container, s));

        if (window.ResizeObserver) {
            s.resizeObserver = new ResizeObserver(() => {
                s.box = fitImageBox(container, s);
                applyLayout(container, s);
            });
            s.resizeObserver.observe(container);
        }

        s.box = fitImageBox(container, s);
        applyLayout(container, s);
    };

    window.dbgSetViewportImageSize = function (containerId, w, h) {
        const container = document.getElementById(containerId);
        if (!container) return;
        const s = getState(container);
        s.imgW = Math.max(1, w | 0);
        s.imgH = Math.max(1, h | 0);
        s.box = fitImageBox(container, s);
        applyLayout(container, s);
    };

    window.dbgSetViewportWarp = function (containerId, wx, wy) {
        const container = document.getElementById(containerId);
        if (!container) return;
        const s = getState(container);
        s.warpX = Math.max(1, wx | 0);
        s.warpY = Math.max(1, wy | 0);
    };

    window.dbgSetViewportMode = function (containerId, mode) {
        const container = document.getElementById(containerId);
        if (!container) return;
        const s = getState(container);
        s.mode = mode;
        if (mode !== 'gpu' && mode !== 'cpu' && mode !== 'debug') s.hoverPixel = null;
        s.box = fitImageBox(container, s);
        applyLayout(container, s);
    };

    window.dbgSetDebugPixel = function (containerId, px, py) {
        const container = document.getElementById(containerId);
        if (!container) return;
        const s = getState(container);
        s.debugPixel = { x: px, y: py };
        refreshOverlay(container, s);
    };

    window.dbgSetViewportThreadStates = function (containerId, states) {
        const container = document.getElementById(containerId);
        if (!container) return;
        const s = getState(container);
        s.threadStates = states;
        refreshOverlay(container, s);
    };

    window.dbgSetClickHandler = function (containerId, handler) {
        const container = document.getElementById(containerId);
        if (!container) return;
        const s = getState(container);
        s.onClick = handler;
    };

    window.dbgResetView = function (containerId) {
        const container = document.getElementById(containerId);
        if (!container) return;
        const s = getState(container);
        s.tx = 0; s.ty = 0; s.scale = 1;
        s.box = fitImageBox(container, s);
        applyLayout(container, s);
    };
})();
