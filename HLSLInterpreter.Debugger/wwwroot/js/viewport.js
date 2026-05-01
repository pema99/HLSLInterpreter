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
                pickMode: 'pixel',
                meshPositions: null,
                meshIndices: null,
                hoverVertex: null,  // { index, x, y } in image-px coords
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

    function projectVertex(s, x, y, z) {
        const vp = window.gpuViewProjection?.(s.imgW, s.imgH);
        if (!vp) return null;
        const cx = vp[0] * x + vp[1] * y + vp[2]  * z + vp[3];
        const cy = vp[4] * x + vp[5] * y + vp[6]  * z + vp[7];
        const cz = vp[8] * x + vp[9] * y + vp[10] * z + vp[11];
        const cw = vp[12]* x + vp[13]* y + vp[14] * z + vp[15];
        if (cw <= 0) return null;
        const ndcX = cx / cw;
        const ndcY = cy / cw;
        const ndcZ = cz / cw;
        if (ndcZ < 0 || ndcZ > 1) return null;
        return {
            x: (ndcX * 0.5 + 0.5) * s.imgW,
            y: (1.0 - (ndcY * 0.5 + 0.5)) * s.imgH,
        };
    }

    function rayTriangle(ox, oy, oz, dx, dy, dz, ax, ay, az, bx, by, bz, cx, cy, cz) {
        const ex1 = bx - ax, ey1 = by - ay, ez1 = bz - az;
        const ex2 = cx - ax, ey2 = cy - ay, ez2 = cz - az;
        const px = dy * ez2 - dz * ey2;
        const py = dz * ex2 - dx * ez2;
        const pz = dx * ey2 - dy * ex2;
        const det = ex1 * px + ey1 * py + ez1 * pz;
        if (Math.abs(det) < 1e-9) return -1;
        const invDet = 1 / det;
        const tx = ox - ax, ty = oy - ay, tz = oz - az;
        const u = (tx * px + ty * py + tz * pz) * invDet;
        if (u < 0 || u > 1) return -1;
        const qx = ty * ez1 - tz * ey1;
        const qy = tz * ex1 - tx * ez1;
        const qz = tx * ey1 - ty * ex1;
        const v = (dx * qx + dy * qy + dz * qz) * invDet;
        if (v < 0 || u + v > 1) return -1;
        const t = (ex2 * qx + ey2 * qy + ez2 * qz) * invDet;
        return t > 1e-5 ? t : -1;
    }

    function nearestVertex(s, imgX, imgY) {
        if (!s.meshPositions || !s.meshIndices) return null;
        const ray = window.gpuPickRay?.(imgX, imgY, s.imgW, s.imgH);
        if (!ray) return null;
        const [ox, oy, oz, dx, dy, dz] = ray;
        const positions = s.meshPositions;
        const indices = s.meshIndices;

        // Closest ray-mesh intersection.
        let bestT = Infinity;
        for (let t = 0; t < indices.length; t += 3) {
            const i0 = indices[t], i1 = indices[t+1], i2 = indices[t+2];
            const hit = rayTriangle(ox, oy, oz, dx, dy, dz,
                positions[i0*3], positions[i0*3+1], positions[i0*3+2],
                positions[i1*3], positions[i1*3+1], positions[i1*3+2],
                positions[i2*3], positions[i2*3+1], positions[i2*3+2]);
            if (hit > 0 && hit < bestT) bestT = hit;
        }
        if (bestT === Infinity) return null;
        const hitX = ox + dx * bestT;
        const hitY = oy + dy * bestT;
        const hitZ = oz + dz * bestT;

        // Vertex with the smallest Euclidean distance to the hit point.
        const n = positions.length / 3;
        let bestIdx = -1, bestD2 = Infinity;
        for (let i = 0; i < n; i++) {
            const ddx = positions[i*3]   - hitX;
            const ddy = positions[i*3+1] - hitY;
            const ddz = positions[i*3+2] - hitZ;
            const d2 = ddx*ddx + ddy*ddy + ddz*ddz;
            if (d2 < bestD2) {
                bestD2 = d2;
                bestIdx = i;
            }
        }
        if (bestIdx < 0) return null;
        const p = projectVertex(s, positions[bestIdx*3], positions[bestIdx*3+1], positions[bestIdx*3+2]);
        if (!p) return null;
        return { index: bestIdx, x: p.x, y: p.y };
    }

    function imgPxToScreen(s, dpr, imgX, imgY) {
        const sx = (s.box.w / s.imgW) * s.scale;
        const sy = (s.box.h / s.imgH) * s.scale;
        return {
            x: (s.box.x + s.tx + imgX * sx) * dpr,
            y: (s.box.y + s.ty + imgY * sy) * dpr,
        };
    }

    function drawTrianglesAroundVertex(ctx, dpr, s, vertexIndex) {
        if (!s.meshIndices || !s.meshPositions) return;
        const positions = s.meshPositions;
        const tx = positions[vertexIndex*3];
        const ty = positions[vertexIndex*3+1];
        const tz = positions[vertexIndex*3+2];
        const eps2 = 1e-10;
        const sharesPos = i => {
            const dx = positions[i*3]   - tx;
            const dy = positions[i*3+1] - ty;
            const dz = positions[i*3+2] - tz;
            return dx*dx + dy*dy + dz*dz <= eps2;
        };

        ctx.save();
        const triCount = (s.meshIndices.length / 3) | 0;
        const corners = new Array(3);
        const drawPass = (color, width) => {
            ctx.lineWidth = width * dpr;
            ctx.strokeStyle = color;
            for (let t = 0; t < triCount; t++) {
                const i0 = s.meshIndices[t * 3 + 0];
                const i1 = s.meshIndices[t * 3 + 1];
                const i2 = s.meshIndices[t * 3 + 2];
                if (!sharesPos(i0) && !sharesPos(i1) && !sharesPos(i2)) continue;
                let allProjected = true;
                for (let c = 0; c < 3; c++) {
                    const idx = [i0, i1, i2][c];
                    const p = projectVertex(s, positions[idx*3], positions[idx*3+1], positions[idx*3+2]);
                    if (!p) { allProjected = false; break; }
                    corners[c] = imgPxToScreen(s, dpr, p.x, p.y);
                }
                if (!allProjected) continue;
                ctx.beginPath();
                ctx.moveTo(corners[0].x, corners[0].y);
                ctx.lineTo(corners[1].x, corners[1].y);
                ctx.lineTo(corners[2].x, corners[2].y);
                ctx.closePath();
                ctx.stroke();
            }
        };
        drawPass('#000', 3);
        drawPass('#f00', 1);
        ctx.restore();
    }

    function drawVertexMarker(ctx, dpr, s, imgX, imgY) {
        const { x: cxScr, y: cyScr } = imgPxToScreen(s, dpr, imgX, imgY);
        ctx.save();
        ctx.lineWidth = 2 * dpr;
        ctx.strokeStyle = '#000';
        ctx.beginPath();
        ctx.arc(cxScr, cyScr, 7 * dpr, 0, Math.PI * 2);
        ctx.stroke();
        ctx.lineWidth = 1 * dpr;
        ctx.strokeStyle = '#0f0';
        ctx.beginPath();
        ctx.arc(cxScr, cyScr, 7 * dpr, 0, Math.PI * 2);
        ctx.stroke();
        ctx.restore();
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
            if (s.pickMode === 'vertex' && s.hoverVertex) {
                drawTrianglesAroundVertex(ctx, dpr, s, s.hoverVertex.index);
                drawVertexMarker(ctx, dpr, s, s.hoverVertex.x, s.hoverVertex.y);
            } else if (s.pickMode !== 'vertex') {
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
        if (s.pickMode === 'vertex' && s.hoverPixel) {
            s.hoverVertex = nearestVertex(s, s.hoverPixel.x, s.hoverPixel.y);
        } else {
            s.hoverVertex = null;
        }
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
        if (s.pickMode === 'vertex') {
            if (!s.hoverVertex) return;
            try { s.onClick(0, 0, s.hoverVertex.index); }
            catch (err) { console.error('[viewport] onClick threw', err); }
            return;
        }
        const px = Math.floor(pt.x);
        const py = Math.floor(pt.y);
        if (px < 0 || py < 0 || px >= s.imgW || py >= s.imgH) return;
        try { s.onClick(px, py); } catch (err) { console.error('[viewport] onClick threw', err); }
    }

    function onMouseLeave(container, s) {
        s.hoverPixel = null;
        s.hoverVertex = null;
        refreshOverlay(container, s);
    }

    window.dbgInitViewport = function (containerId) {
        const container = document.getElementById(containerId);
        if (!container || container.__viewportInit) return;
        container.__viewportInit = true;
        const s = getState(container);

        container.addEventListener('wheel', e => onWheel(container, s, e), { passive: false });
        container.addEventListener('mousedown', e => onMouseDown(container, s, e));

        // Drop an OBJ file onto the viewport to load it as the current mesh.
        container.addEventListener('dragover', e => {
            if (e.dataTransfer) e.dataTransfer.dropEffect = 'copy';
            e.preventDefault();
        });
        container.addEventListener('drop', e => {
            const file = e.dataTransfer && e.dataTransfer.files && e.dataTransfer.files[0];
            if (!file) return;
            e.preventDefault();
            const reader = new FileReader();
            reader.onload = ev => {
                if (!window._dotNetDebugRef) return;
                window._dotNetDebugRef.invokeMethodAsync('LoadObjMesh', ev.target.result);
            };
            reader.readAsText(file);
        });
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

    window.dbgSetViewportPickMode = function (containerId, pickMode, positions, indices) {
        const container = document.getElementById(containerId);
        if (!container) return;
        const s = getState(container);
        s.pickMode = pickMode === 'vertex' ? 'vertex' : 'pixel';
        s.meshPositions = positions || null;
        s.meshIndices = indices || null;
        if (s.pickMode !== 'vertex') s.hoverVertex = null;
        container.classList.toggle('pick-vertex', s.pickMode === 'vertex');
        refreshOverlay(container, s);
    };

    window.dbgRefreshViewportOverlay = function (containerId) {
        const container = document.getElementById(containerId);
        if (!container) return;
        const s = getState(container);
        if (s.pickMode === 'vertex' && s.hoverPixel) {
            s.hoverVertex = nearestVertex(s, s.hoverPixel.x, s.hoverPixel.y);
        }
        refreshOverlay(container, s);
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
