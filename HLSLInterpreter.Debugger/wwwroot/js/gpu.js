// GPU preview + click-to-debug. ES module so it can import slang-wasm.js.

const SLANG_STAGE_VERTEX = 1;
const SLANG_STAGE_FRAGMENT = 5;

// Slang's numeric target IDs shift between versions, so look up "WGSL" by name.
function findTargetValue(slang, name) {
    const targets = slang.getCompileTargets();
    if (!targets) throw new Error('Slang: getCompileTargets returned null');
    if (Array.isArray(targets)) {
        for (const t of targets) if (t.name === name) return t.value;
    } else if (typeof targets.size === 'function') {
        for (let i = 0; i < targets.size(); i++) {
            const t = targets.get(i);
            if (t && t.name === name) return t.value;
        }
    } else {
        for (const k of Object.keys(targets)) {
            const t = targets[k];
            if (t && t.name === name) return t.value;
        }
    }
    throw new Error(`Slang: compile target '${name}' not found in getCompileTargets()`);
}

// Vertex buffer layout for vert+frag mode: pos3 + normal3 + uv2 = 32 bytes.
const MESH_VERTEX_STRIDE = 32;

function matIdentity() {
    return [1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1];
}
function matMul(A, B) {
    const C = new Array(16);
    for (let r = 0; r < 4; r++) {
        for (let c = 0; c < 4; c++) {
            let s = 0;
            for (let k = 0; k < 4; k++) s += A[r*4+k] * B[k*4+c];
            C[r*4+c] = s;
        }
    }
    return C;
}
// Right-handed perspective with depth [0,1], Camera looks down -Z.
function matPerspective(fovY, aspect, near, far) {
    const f = 1 / Math.tan(fovY / 2);
    const a = far / (near - far);
    const b = (near * far) / (near - far);
    return [
        f / aspect, 0, 0, 0,
        0,          f, 0, 0,
        0,          0, a, b,
        0,          0,-1, 0,
    ];
}
function matLookAt(ex, ey, ez, tx, ty, tz, ux, uy, uz) {
    let fx = tx - ex, fy = ty - ey, fz = tz - ez;
    let fl = Math.hypot(fx, fy, fz) || 1;
    fx /= fl; fy /= fl; fz /= fl;
    let rx = fy * uz - fz * uy;
    let ry = fz * ux - fx * uz;
    let rz = fx * uy - fy * ux;
    let rl = Math.hypot(rx, ry, rz) || 1;
    rx /= rl; ry /= rl; rz /= rl;
    const u2x = ry * fz - rz * fy;
    const u2y = rz * fx - rx * fz;
    const u2z = rx * fy - ry * fx;
    return [
         rx,  ry,  rz, -(rx*ex + ry*ey + rz*ez),
        u2x, u2y, u2z, -(u2x*ex+ u2y*ey+ u2z*ez),
        -fx, -fy, -fz,  (fx*ex + fy*ey + fz*ez),
         0,   0,   0,   1,
    ];
}
function writeMat4(out, offset, m) {
    for (let i = 0; i < 16; i++) out[offset + i] = m[i];
}

let slangPromise = null;
let webgpuPromise = null;
let testPreamblePromise = null;

function getTestPreamble() {
    if (!testPreamblePromise) {
        const url = new URL('../lib/HLSLTest.hlsl', import.meta.url);
        testPreamblePromise = fetch(url).then(r => {
            if (!r.ok) throw new Error('Failed to fetch HLSLTest.hlsl: ' + r.status);
            return r.text();
        });
    }
    return testPreamblePromise;
}
let active = null;

function getSlang() {
    if (!slangPromise) {
        slangPromise = import('../lib/slang/slang-wasm.js')
            .then(mod => mod.default());
    }
    return slangPromise;
}

function getDevice() {
    if (!webgpuPromise) {
        webgpuPromise = (async () => {
            if (!('gpu' in navigator)) throw new Error('WebGPU is not supported in this browser.');
            const adapter = await navigator.gpu.requestAdapter();
            if (!adapter) throw new Error('No WebGPU adapter available.');
            const device = await adapter.requestDevice();
            device.addEventListener?.('uncapturederror', e => console.error('[WebGPU]', e.error?.message || e));
            return device;
        })();
    }
    return webgpuPromise;
}

function fitCanvas(canvas) {
    const cw = canvas.clientWidth;
    const ch = canvas.clientHeight;
    if (cw <= 0 || ch <= 0) return;
    const dpr = window.devicePixelRatio || 1;
    const tw = Math.max(1, Math.floor(cw * dpr));
    const th = Math.max(1, Math.floor(ch * dpr));
    if (canvas.width !== tw) canvas.width = tw;
    if (canvas.height !== th) canvas.height = th;
}

function extractEntryPoints(wgsl) {
    const vs = wgsl.match(/@vertex\s+fn\s+([A-Za-z_][A-Za-z0-9_]*)/);
    const fs = wgsl.match(/@fragment\s+fn\s+([A-Za-z_][A-Za-z0-9_]*)/);
    return { vsEntry: vs ? vs[1] : null, fsEntry: fs ? fs[1] : null };
}

// Camera state
const CAMERA_FOV_Y = 60 * Math.PI / 180;
let cameraYaw = 0.6;
let cameraPitch = 0.3;
let cameraDistance = 2.5;

let mouseX = 0;
let mouseY = 0;
let mouseLeft = 0;
let mouseRight = 0;
let mouseRightHeld = false;

function redrawIfPaused() {
    if (!active || active.running) return;
    const fakeNow = active.startTimeMs + (active.lastTime || 0) * 1000;
    drawFrame(active, fakeNow);
}

function mouseEventInsideCanvas(e) {
    if (!active) return null;
    const rect = active.canvas.getBoundingClientRect();
    if (rect.width <= 0 || rect.height <= 0) return null;
    if (e.clientX < rect.left || e.clientX > rect.right) return null;
    if (e.clientY < rect.top || e.clientY > rect.bottom) return null;
    return rect;
}

function updateMousePosFromEvent(e, rect) {
    mouseX = (e.clientX - rect.left) * (active.canvas.width / rect.width);
    mouseY = active.canvas.height - (e.clientY - rect.top) * (active.canvas.height / rect.height);
}

window.addEventListener('mousemove', e => {
    if (!mouseRightHeld) return;
    const rect = mouseEventInsideCanvas(e);
    if (!rect) return;
    updateMousePosFromEvent(e, rect);
    redrawIfPaused();
});
window.addEventListener('mousedown', e => {
    if (e.button === 2) mouseRightHeld = true;
    if (!mouseRightHeld) return;
    const rect = mouseEventInsideCanvas(e);
    if (!rect) return;
    updateMousePosFromEvent(e, rect);
    if (e.button === 0) mouseLeft = 1;
    if (e.button === 2) mouseRight = 1;
    redrawIfPaused();
});
window.addEventListener('mouseup', e => {
    if (mouseRightHeld) {
        const rect = mouseEventInsideCanvas(e);
        if (rect) {
            updateMousePosFromEvent(e, rect);
            if (e.button === 0) mouseLeft = 0;
            if (e.button === 2) mouseRight = 0;
            redrawIfPaused();
        }
    }
    if (e.button === 2) mouseRightHeld = false;
});

window.gpuMouse = function () {
    return [mouseX, mouseY, mouseRight, mouseLeft];
};

window.gpuView = function () {
    const cp = Math.cos(cameraPitch), sp = Math.sin(cameraPitch);
    const cy = Math.cos(cameraYaw),   sy = Math.sin(cameraYaw);
    const ex = sy * cp * cameraDistance;
    const ey = sp * cameraDistance;
    const ez = cy * cp * cameraDistance;
    return matLookAt(ex, ey, ez, 0, 0, 0, 0, 1, 0);
};

window.gpuProjection = function (canvasW, canvasH) {
    const aspect = Math.max(1e-4, canvasW / Math.max(1, canvasH));
    return matPerspective(CAMERA_FOV_Y, aspect, 0.1, 100);
};

window.gpuViewProjection = function (canvasW, canvasH) {
    return matMul(window.gpuProjection(canvasW, canvasH), window.gpuView());
};

window.gpuPickRay = function (imgX, imgY, imgW, imgH) {
    const cp = Math.cos(cameraPitch), sp = Math.sin(cameraPitch);
    const cy = Math.cos(cameraYaw),   sy = Math.sin(cameraYaw);
    const ox = sy * cp * cameraDistance;
    const oy = sp * cameraDistance;
    const oz = cy * cp * cameraDistance;
    // forward = normalize(target - origin), target is origin (0,0,0)
    let fx = -ox, fy = -oy, fz = -oz;
    let fl = Math.hypot(fx, fy, fz) || 1;
    fx /= fl; fy /= fl; fz /= fl;
    // right = normalize(forward x worldUp), worldUp = (0,1,0)
    let rx = -fz, ry = 0, rz = fx;
    let rl = Math.hypot(rx, ry, rz) || 1;
    rx /= rl; ry /= rl; rz /= rl;
    // up = right x forward
    const ux = ry * fz - rz * fy;
    const uy = rz * fx - rx * fz;
    const uz = rx * fy - ry * fx;
    const halfH = Math.tan(CAMERA_FOV_Y / 2);
    const halfW = halfH * Math.max(1e-4, imgW / Math.max(1, imgH));
    const ndcX = (imgX / imgW) * 2 - 1;
    const ndcY = 1 - (imgY / imgH) * 2;
    let dx = fx + ndcX * halfW * rx + ndcY * halfH * ux;
    let dy = fy + ndcX * halfW * ry + ndcY * halfH * uy;
    let dz = fz + ndcX * halfW * rz + ndcY * halfH * uz;
    const dl = Math.hypot(dx, dy, dz) || 1;
    dx /= dl; dy /= dl; dz /= dl;
    return [ox, oy, oz, dx, dy, dz];
};

// globalSession is heavy, so we cache it
let slangGlobalPromise = null;
async function getSlangGlobal() {
    if (!slangGlobalPromise) {
        slangGlobalPromise = (async () => {
            const slang = await getSlang();
            const globalSession = slang.createGlobalSession();
            if (!globalSession) throw new Error('Slang: createGlobalSession failed: ' + (slang.getLastError()?.message || ''));
            const wgslTarget = findTargetValue(slang, 'WGSL');
            return { slang, globalSession, wgslTarget };
        })();
    }
    return slangGlobalPromise;
}

async function compileToWgsl(hlslSource, vertEntryName, fragEntryName) {
    const [{ slang, globalSession, wgslTarget }, testPreamble] =
        await Promise.all([getSlangGlobal(), getTestPreamble()]);

    const session = globalSession.createSession(wgslTarget);
    if (!session) throw new Error('Slang: createSession(WGSL) failed: ' + (slang.getLastError()?.message || ''));

    const fullSource = testPreamble + '\n' + hlslSource;
    let userModule = null, vs = null, fs = null, composite = null, linked = null;
    try {
        userModule = session.loadModuleFromSource(fullSource, 'user', 'user.slang');
        if (!userModule) {
            const e = slang.getLastError();
            throw new Error('Slang compile error:\n' + (e?.message || 'unknown'));
        }
        vs = userModule.findAndCheckEntryPoint(vertEntryName, SLANG_STAGE_VERTEX);
        if (!vs) throw new Error(`Slang: vertex entry '${vertEntryName}' not found: ` + (slang.getLastError()?.message || ''));
        fs = userModule.findAndCheckEntryPoint(fragEntryName, SLANG_STAGE_FRAGMENT);
        if (!fs) throw new Error(`Slang: fragment entry '${fragEntryName}' not found: ` + (slang.getLastError()?.message || ''));
        composite = session.createCompositeComponentType([userModule, vs, fs]);
        if (!composite) throw new Error('Slang: createCompositeComponentType failed: ' + (slang.getLastError()?.message || ''));
        linked = composite.link();
        if (!linked) throw new Error('Slang: link failed: ' + (slang.getLastError()?.message || ''));
        const wgsl = linked.getTargetCode(0);
        if (!wgsl) throw new Error('Slang: getTargetCode returned empty: ' + (slang.getLastError()?.message || ''));
        return wgsl;
    } finally {
        const tryDelete = h => { try { h && h.delete && h.delete(); } catch (_) {} };
        tryDelete(linked);
        tryDelete(composite);
        tryDelete(vs);
        tryDelete(fs);
        tryDelete(userModule);
        tryDelete(session);
    }
}

function attachResizeObserver(canvas) {
    if (canvas.__dbgResizeAttached || !window.ResizeObserver) return;
    canvas.__dbgResizeAttached = true;
    const ro = new ResizeObserver(() => {
        // Skip when stopped, otherwise stale GPU dimensions would clobber the
        // CPU canvas's image size and stretch its displayed pixels on resize.
        if (!active || active.canvas !== canvas) return;
        if (!active.running && !active.paused) return;
        fitCanvas(canvas);
        if (typeof window.dbgSetViewportImageSize === 'function') {
            window.dbgSetViewportImageSize('image-container', canvas.width, canvas.height);
        }
        if (active.paused) redrawIfPaused();
    });
    ro.observe(canvas.parentElement || canvas);
}

function scheduleFrame() {
    if (!active || !active.running) return;
    active.animFrameId = requestAnimationFrame(renderFrame);
}

function ensureDepthTexture(r) {
    const w = r.canvas.width, h = r.canvas.height;
    if (r.depthTexture && r.depthTexture.width === w && r.depthTexture.height === h) return;
    if (r.depthTexture) r.depthTexture.destroy?.();
    r.depthTexture = r.device.createTexture({
        size: [w, h],
        format: 'depth24plus',
        usage: GPUTextureUsage.RENDER_ATTACHMENT,
    });
}

function drawFrame(r, now) {
    const prevW = r.canvas.width, prevH = r.canvas.height;
    fitCanvas(r.canvas);
    if ((r.canvas.width !== prevW || r.canvas.height !== prevH)
            && typeof window.dbgSetViewportImageSize === 'function') {
        window.dbgSetViewportImageSize('image-container', r.canvas.width, r.canvas.height);
    }

    const t = (now - r.startTimeMs) / 1000;
    r.lastTime = t;

    const u = new Float32Array(44);
    u[0] = r.warpX;
    u[1] = r.warpY;
    u[2] = r.canvas.width;
    u[3] = r.canvas.height;
    u[4] = t;

    const viewMat = r.renderMode === 'vertfrag' ? window.gpuView() : matIdentity();
    const projMat = r.renderMode === 'vertfrag'
        ? window.gpuProjection(r.canvas.width, r.canvas.height)
        : matIdentity();
    writeMat4(u, 8, viewMat);
    writeMat4(u, 24, projMat);
    u[40] = mouseX;
    u[41] = mouseY;
    u[42] = mouseRight;
    u[43] = mouseLeft;
    r.device.queue.writeBuffer(r.uniformBuffer, 0, u);

    let view;
    try { view = r.context.getCurrentTexture().createView(); }
    catch (e) { return false; }

    const colorAttachment = {
        view,
        clearValue: { r: 0, g: 0, b: 0, a: 1 },
        loadOp: 'clear',
        storeOp: 'store',
    };
    const enc = r.device.createCommandEncoder();
    let depthAttachment = undefined;
    if (r.renderMode === 'vertfrag') {
        ensureDepthTexture(r);
        depthAttachment = {
            view: r.depthTexture.createView(),
            depthClearValue: 1.0,
            depthLoadOp: 'clear',
            depthStoreOp: 'store',
        };
    }
    const pass = enc.beginRenderPass({
        colorAttachments: [colorAttachment],
        depthStencilAttachment: depthAttachment,
    });
    pass.setPipeline(r.pipeline);
    pass.setBindGroup(0, r.bindGroup);
    if (r.renderMode === 'vertfrag') {
        pass.setVertexBuffer(0, r.meshVB);
        pass.setIndexBuffer(r.meshIB, 'uint32');
        pass.drawIndexed(r.meshIndexCount);
    } else {
        pass.draw(3);
    }
    pass.end();
    r.device.queue.submit([enc.finish()]);
    return true;
}

function renderFrame(now) {
    const r = active;
    if (!r || !r.running) return;
    if (!drawFrame(r, now)) {
        r.running = false;
        return;
    }
    scheduleFrame();
}

window.gpuIsAvailable = function () {
    return 'gpu' in navigator;
};

window.gpuStop = function () {
    if (!active) return;
    active.running = false;
    active.paused = false;
    if (active.animFrameId) cancelAnimationFrame(active.animFrameId);
    active.animFrameId = null;
};

window.gpuPause = function () {
    if (!active || !active.running) return;
    active.running = false;
    active.paused = true;
    if (active.animFrameId) cancelAnimationFrame(active.animFrameId);
    active.animFrameId = null;
};

window.gpuResume = function () {
    if (!active || active.running) return;
    // Rebase startTimeMs so _Time picks up where it left off.
    active.startTimeMs = performance.now() - (active.lastTime || 0) * 1000;
    active.running = true;
    active.paused = false;
    scheduleFrame();
};

window.gpuRestart = function () {
    if (!active) return;
    active.startTimeMs = performance.now();
    active.lastTime = 0;
    // If paused, draw one frame at t=0 so the user sees the reset without
    // changing the pause state.
    if (!active.running) drawFrame(active, active.startTimeMs);
};

// Live canvas size, time, and camera state so a Debug-button entry can
// reproduce the _Resolution, _Time, and view-projection matrix the GPU saw.
window.gpuSnapshot = function () {
    if (!active) return null;
    return [
        active.lastTime || 0,
        active.canvas.width,
        active.canvas.height,
    ];
};

function createMeshBuffers(device, meshVertices, meshIndices) {
    const verts = meshVertices instanceof Float32Array
        ? meshVertices : new Float32Array(meshVertices);
    const idx = meshIndices instanceof Uint32Array
        ? meshIndices : new Uint32Array(meshIndices);
    const ibSize = (idx.byteLength + 3) & ~3;
    const vb = device.createBuffer({
        size: verts.byteLength,
        usage: GPUBufferUsage.VERTEX | GPUBufferUsage.COPY_DST,
    });
    device.queue.writeBuffer(vb, 0, verts);
    const ib = device.createBuffer({
        size: ibSize,
        usage: GPUBufferUsage.INDEX | GPUBufferUsage.COPY_DST,
    });
    device.queue.writeBuffer(ib, 0, idx);
    return { vb, ib, indexCount: idx.length };
}

function attachCameraInput() {
    const container = document.getElementById('image-container');
    if (!container || container.__dbgCameraInput) return;
    container.__dbgCameraInput = true;

    let dragging = false;
    let lastX = 0, lastY = 0;

    const isVertFrag = () => active && active.renderMode === 'vertfrag';

    // Right click to rotate
    container.addEventListener('mousedown', (e) => {
        if (!isVertFrag() || e.button !== 2) return;
        e.preventDefault();
        e.stopPropagation();
        dragging = true;
        lastX = e.clientX;
        lastY = e.clientY;
    }, true);
    window.addEventListener('mousemove', (e) => {
        if (!dragging || !isVertFrag()) return;
        const dx = e.clientX - lastX;
        const dy = e.clientY - lastY;
        lastX = e.clientX;
        lastY = e.clientY;
        cameraYaw -= dx * 0.01;
        cameraPitch = Math.max(-1.4, Math.min(1.4, cameraPitch + dy * 0.01));
        redrawIfPaused();
        window.dbgRefreshViewportOverlay?.('image-container');
    });

    // Stop rotate
    window.addEventListener('mouseup', (e) => {
        if (e.button === 2) dragging = false;
    });
    // Prevent right click menu
    container.addEventListener('contextmenu', (e) => {
        if (isVertFrag()) e.preventDefault();
    });

    // Scroll + right click to zoom camera
    container.addEventListener('wheel', (e) => {
        if (!dragging || !isVertFrag()) return;
        e.preventDefault();
        e.stopPropagation();
        const k = Math.exp(e.deltaY * 0.0015);
        cameraDistance = Math.max(0.5, Math.min(100, cameraDistance * k));
        redrawIfPaused();
        window.dbgRefreshViewportOverlay?.('image-container');
    }, { capture: true, passive: false });
}

// Map (semanticBase, semanticIndex) → byte offset within the interleaved
// vertex (matches Mesh.GetInterleavedVertices: pos3 + normal3 + uv2).
const MESH_OFFSET_BY_SEMANTIC = {
    'POSITION_0': 0,
    'NORMAL_0':   12,
    'TEXCOORD_0': 24,
};

const VERTEX_FORMAT_BY_DIM = ['float32', 'float32x2', 'float32x3', 'float32x4'];

function buildMeshAttributes(vertexInputs) {
    if (!Array.isArray(vertexInputs)) return [];
    return vertexInputs.map((input, i) => {
        const key = `${input.semanticBase}_${input.semanticIndex}`;
        const offset = MESH_OFFSET_BY_SEMANTIC[key];
        if (offset === undefined) {
            throw new Error(
                `Vertex input '${key}' is not provided by the mesh. ` +
                `Available: POSITION, NORMAL, TEXCOORD0.`);
        }
        const format = VERTEX_FORMAT_BY_DIM[input.dimensions - 1];
        if (!format) {
            throw new Error(`Vertex input '${key}' has unsupported dimension ${input.dimensions}.`);
        }
        return { shaderLocation: i, offset, format };
    });
}

window.gpuRender = async function (canvasId, hlslSource, entryPoint, warpX, warpY, dotNetRef, renderMode, vertexEntryName, vertexInputs, meshVertices, meshIndices, initialTime) {
    if (!('gpu' in navigator)) throw new Error('WebGPU is not supported in this browser.');

    const canvas = document.getElementById(canvasId);
    if (!canvas) throw new Error('Canvas not found: ' + canvasId);

    const mode = renderMode === 'vertfrag' ? 'vertfrag' : 'pixel';
    const vsName = vertexEntryName || (mode === 'vertfrag' ? 'vert' : '_dbgVertex');

    window.gpuStop();
    if (active && active.canvas === canvas) {
        try { active.uniformBuffer?.destroy?.(); } catch (_) {}
        try { active.depthTexture?.destroy?.(); } catch (_) {}
        try { active.meshVB?.destroy?.(); } catch (_) {}
        try { active.meshIB?.destroy?.(); } catch (_) {}
    }

    const wgsl = await compileToWgsl(hlslSource, vsName, entryPoint);
    const { vsEntry, fsEntry } = extractEntryPoints(wgsl);
    if (!vsEntry || !fsEntry) {
        throw new Error('Could not locate @vertex/@fragment entry points in compiled WGSL.');
    }

    const device = await getDevice();
    const format = navigator.gpu.getPreferredCanvasFormat();

    let context = canvas.__dbgContext;
    if (!context) {
        context = canvas.getContext('webgpu');
        if (!context) throw new Error("getContext('webgpu') returned null.");
        context.configure({ device, format, alphaMode: 'opaque' });
        canvas.__dbgContext = context;
        attachResizeObserver(canvas);
    }
    fitCanvas(canvas);

    // imagestate.js owns viewport mode and click handlers. We only push the
    // live canvas size, since we own the GPU render target's dimensions.
    if (typeof window.dbgInitViewport === 'function')
        window.dbgInitViewport('image-container');
    if (typeof window.dbgSetViewportImageSize === 'function')
        window.dbgSetViewportImageSize('image-container', canvas.width, canvas.height);

    const shaderModule = device.createShaderModule({ code: wgsl });

    const info = await shaderModule.getCompilationInfo?.();
    if (info && info.messages) {
        const errors = info.messages.filter(m => m.type === 'error');
        if (errors.length > 0) {
            throw new Error('WGSL compile errors:\n' + errors.map(m => `  ${m.message}`).join('\n'));
        }
    }

    const uniformBuffer = device.createBuffer({
        size: 176,
        usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST,
    });

    const bindGroupLayout = device.createBindGroupLayout({
        entries: [{
            binding: 0,
            visibility: GPUShaderStage.VERTEX | GPUShaderStage.FRAGMENT,
            buffer: { type: 'uniform' },
        }],
    });
    const pipelineLayout = device.createPipelineLayout({ bindGroupLayouts: [bindGroupLayout] });

    let pipeline, meshVB = null, meshIB = null, meshIndexCount = 0;
    if (mode === 'vertfrag') {
        if (!meshVertices || !meshIndices)
            throw new Error('vert+frag mode requires mesh vertex/index data.');
        const buffers = createMeshBuffers(device, meshVertices, meshIndices);
        meshVB = buffers.vb;
        meshIB = buffers.ib;
        meshIndexCount = buffers.indexCount;
        const meshAttributes = buildMeshAttributes(vertexInputs);
        pipeline = device.createRenderPipeline({
            layout: pipelineLayout,
            vertex: {
                module: shaderModule,
                entryPoint: vsEntry,
                buffers: meshAttributes.length === 0 ? [] : [{
                    arrayStride: MESH_VERTEX_STRIDE,
                    attributes: meshAttributes,
                }],
            },
            fragment: { module: shaderModule, entryPoint: fsEntry, targets: [{ format }] },
            primitive: { topology: 'triangle-list', cullMode: 'back' },
            depthStencil: { format: 'depth24plus', depthWriteEnabled: true, depthCompare: 'less-equal' },
        });
    } else {
        pipeline = device.createRenderPipeline({
            layout: pipelineLayout,
            vertex: { module: shaderModule, entryPoint: vsEntry },
            fragment: { module: shaderModule, entryPoint: fsEntry, targets: [{ format }] },
            primitive: { topology: 'triangle-list' },
        });
    }

    const bindGroup = device.createBindGroup({
        layout: bindGroupLayout,
        entries: [{ binding: 0, resource: { buffer: uniformBuffer } }],
    });

    active = {
        canvas, context, device, pipeline, bindGroup, uniformBuffer,
        warpX, warpY, dotNetRef,
        renderMode: mode,
        meshVB, meshIB, meshIndexCount,
        depthTexture: null,
        startTimeMs: performance.now() - (initialTime || 0) * 1000,
        lastTime: initialTime || 0,
        running: true,
        animFrameId: null,
    };
    attachCameraInput();
    scheduleFrame();
};
