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

// The HLSL preamble (debugger globals + dbgVertex + DbgMeshVertex) is owned by
// C# (ShaderAssembler) so the wrapper-generation pass can parse the same source
// the GPU compiles. JS just compiles whatever it gets, prepending only the
// fetched HLSLTest macros that ASSERT/PRINTF expand to.

// Unit cube: 24 verts (4 per face for distinct normals + UVs), 32-byte stride
// (pos3 + normal3 + uv2). 36 indices, uint16. Built once and uploaded lazily.
const CUBE_VERTICES = (() => {
    const corners = [
        [-1,-1,-1],[1,-1,-1],[1,1,-1],[-1,1,-1],
        [-1,-1, 1],[1,-1, 1],[1,1, 1],[-1,1, 1],
    ];
    // [BL_idx, BR_idx, TR_idx, TL_idx, nx, ny, nz] — BL/BR/TR/TL viewed from outside,
    // CCW so cullMode='back' culls the inside. Verified by cross-product of edges.
    const faces = [
        [4,5,6,7,  0, 0, 1], // +Z front
        [1,0,3,2,  0, 0,-1], // -Z back
        [5,1,2,6,  1, 0, 0], // +X right
        [0,4,7,3, -1, 0, 0], // -X left
        [3,7,6,2,  0, 1, 0], // +Y top
        [0,1,5,4,  0,-1, 0], // -Y bottom
    ];
    const uvs = [[0,0],[1,0],[1,1],[0,1]];
    const out = new Float32Array(24 * 8);
    let o = 0;
    for (const f of faces) {
        const corns = [corners[f[0]], corners[f[1]], corners[f[2]], corners[f[3]]];
        for (let i = 0; i < 4; i++) {
            out[o++] = corns[i][0]; out[o++] = corns[i][1]; out[o++] = corns[i][2];
            out[o++] = f[4];        out[o++] = f[5];        out[o++] = f[6];
            out[o++] = uvs[i][0];   out[o++] = uvs[i][1];
        }
    }
    return out;
})();

const CUBE_INDICES = (() => {
    const out = new Uint16Array(36);
    for (let f = 0; f < 6; f++) {
        const b = f * 4, o = f * 6;
        out[o] = b; out[o+1] = b+1; out[o+2] = b+2;
        out[o+3] = b; out[o+4] = b+2; out[o+5] = b+3;
    }
    return out;
})();

// Row-major matrix math. Transposed to column-major at upload time.
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
// Right-handed perspective with depth [0,1] (D3D/WebGPU clip space). Camera looks down -Z.
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
// Slang's default for HLSL→WGSL uses row-major cbuffer matrix storage, so we
// upload our row-major math arrays as-is.
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

// globalSession is heavy, so we cache it. The per-target session caches modules
// by name internally, so we recreate it per compile to avoid unbounded growth.
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
        if (!active || !active.running || active.canvas !== canvas) return;
        fitCanvas(canvas);
        if (typeof window.dbgSetViewportImageSize === 'function') {
            window.dbgSetViewportImageSize('image-container', canvas.width, canvas.height);
        }
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

    // Cbuffer layout: scalars in floats 0..4, padding 5..7 (mat4 needs 16-byte
    // alignment), 4x4 matrix in floats 8..23. Total 96 bytes.
    const u = new Float32Array(24);
    u[0] = r.warpX;
    u[1] = r.warpY;
    u[2] = r.canvas.width;
    u[3] = r.canvas.height;
    u[4] = t;

    let viewProj;
    if (r.renderMode === 'vertfrag') {
        const aspect = Math.max(1e-4, r.canvas.width / r.canvas.height);
        const cp = Math.cos(r.cameraPitch), sp = Math.sin(r.cameraPitch);
        const cy = Math.cos(r.cameraYaw),   sy = Math.sin(r.cameraYaw);
        const ex = sy * cp * r.cameraDistance;
        const ey = sp * r.cameraDistance;
        const ez = cy * cp * r.cameraDistance;
        const proj = matPerspective(60 * Math.PI / 180, aspect, 0.1, 100);
        const view = matLookAt(ex, ey, ez, 0, 0, 0, 0, 1, 0);
        viewProj = matMul(proj, view);
    } else {
        viewProj = matIdentity();
    }
    writeMat4(u, 8, viewProj);
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
        pass.setVertexBuffer(0, r.cubeVB);
        pass.setIndexBuffer(r.cubeIB, 'uint16');
        pass.drawIndexed(36);
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
    if (active.animFrameId) cancelAnimationFrame(active.animFrameId);
    active.animFrameId = null;
};

window.gpuPause = function () {
    if (!active || !active.running) return;
    active.running = false;
    if (active.animFrameId) cancelAnimationFrame(active.animFrameId);
    active.animFrameId = null;
};

window.gpuResume = function () {
    if (!active || active.running) return;
    // Rebase startTimeMs so _Time picks up where it left off.
    active.startTimeMs = performance.now() - (active.lastTime || 0) * 1000;
    active.running = true;
    scheduleFrame();
};

window.gpuRestart = function () {
    if (!active) return;
    active.startTimeMs = performance.now();
    active.lastTime = 0;
    // If paused, draw one frame at t=0 so the user sees the reset without
    // changing the pause state.
    if (!active.running) drawFrame(active, performance.now());
};

// Live canvas size + elapsed time, so a Debug-button entry can reproduce the
// _Resolution and _Time the GPU saw.
window.gpuSnapshot = function () {
    if (!active) return null;
    return [active.lastTime || 0, active.canvas.width, active.canvas.height];
};

function ensureCubeBuffers(device) {
    if (device.__dbgCubeVB && device.__dbgCubeIB) {
        return { vb: device.__dbgCubeVB, ib: device.__dbgCubeIB };
    }
    const vb = device.createBuffer({
        size: CUBE_VERTICES.byteLength,
        usage: GPUBufferUsage.VERTEX | GPUBufferUsage.COPY_DST,
    });
    device.queue.writeBuffer(vb, 0, CUBE_VERTICES);
    const ib = device.createBuffer({
        size: CUBE_INDICES.byteLength,
        usage: GPUBufferUsage.INDEX | GPUBufferUsage.COPY_DST,
    });
    device.queue.writeBuffer(ib, 0, CUBE_INDICES);
    device.__dbgCubeVB = vb;
    device.__dbgCubeIB = ib;
    return { vb, ib };
}

function attachCameraInput() {
    const container = document.getElementById('image-container');
    if (!container || container.__dbgCameraInput) return;
    container.__dbgCameraInput = true;

    let dragging = false;
    let lastX = 0, lastY = 0;

    const isVertFrag = () => active && active.renderMode === 'vertfrag';
    const redrawIfPaused = () => {
        if (active && !active.running) drawFrame(active, performance.now());
    };

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
        active.cameraYaw -= dx * 0.01;
        active.cameraPitch = Math.max(-1.4, Math.min(1.4, active.cameraPitch + dy * 0.01));
        redrawIfPaused();
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
        active.cameraDistance = Math.max(0.5, Math.min(100, active.cameraDistance * k));
        redrawIfPaused();
    }, { capture: true, passive: false });
}

// Cube vertex buffer is laid out as pos(3f) + normal(3f) + uv(2f) = 32 bytes.
// Map (semanticBase, semanticIndex) → byte offset of that attribute.
const CUBE_OFFSET_BY_SEMANTIC = {
    'POSITION_0': 0,
    'NORMAL_0':   12,
    'TEXCOORD_0': 24,
};

const VERTEX_FORMAT_BY_DIM = ['float32', 'float32x2', 'float32x3', 'float32x4'];

function buildCubeAttributes(vertexInputs) {
    if (!Array.isArray(vertexInputs)) return [];
    return vertexInputs.map((input, i) => {
        const key = `${input.semanticBase}_${input.semanticIndex}`;
        const offset = CUBE_OFFSET_BY_SEMANTIC[key];
        if (offset === undefined) {
            throw new Error(
                `Vertex input '${key}' is not provided by the debugger cube. ` +
                `Available: POSITION, NORMAL, TEXCOORD0.`);
        }
        const format = VERTEX_FORMAT_BY_DIM[input.dimensions - 1];
        if (!format) {
            throw new Error(`Vertex input '${key}' has unsupported dimension ${input.dimensions}.`);
        }
        return { shaderLocation: i, offset, format };
    });
}

window.gpuRender = async function (canvasId, hlslSource, entryPoint, warpX, warpY, dotNetRef, renderMode, vertexEntryName, vertexInputs) {
    if (!('gpu' in navigator)) throw new Error('WebGPU is not supported in this browser.');

    const canvas = document.getElementById(canvasId);
    if (!canvas) throw new Error('Canvas not found: ' + canvasId);

    const mode = renderMode === 'vertfrag' ? 'vertfrag' : 'pixel';
    const vsName = mode === 'vertfrag' ? (vertexEntryName || 'vert') : 'dbgVertex';

    // Carry over camera state across reruns so reruns don't snap the cube back.
    const prevCamera = (active && active.canvas === canvas)
        ? { yaw: active.cameraYaw, pitch: active.cameraPitch, distance: active.cameraDistance }
        : null;

    window.gpuStop();
    // Drop GPU resources from the prior run (cube buffers are cached on the
    // device and intentionally retained).
    if (active && active.canvas === canvas) {
        try { active.uniformBuffer?.destroy?.(); } catch (_) {}
        try { active.depthTexture?.destroy?.(); } catch (_) {}
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
        size: 96,
        usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST,
    });

    let pipeline, cubeVB = null, cubeIB = null;
    if (mode === 'vertfrag') {
        const buffers = ensureCubeBuffers(device);
        cubeVB = buffers.vb;
        cubeIB = buffers.ib;
        const cubeAttributes = buildCubeAttributes(vertexInputs);
        pipeline = device.createRenderPipeline({
            layout: 'auto',
            vertex: {
                module: shaderModule,
                entryPoint: vsEntry,
                buffers: cubeAttributes.length === 0 ? [] : [{
                    arrayStride: 32,
                    attributes: cubeAttributes,
                }],
            },
            fragment: { module: shaderModule, entryPoint: fsEntry, targets: [{ format }] },
            primitive: { topology: 'triangle-list', cullMode: 'back' },
            depthStencil: { format: 'depth24plus', depthWriteEnabled: true, depthCompare: 'less-equal' },
        });
    } else {
        pipeline = device.createRenderPipeline({
            layout: 'auto',
            vertex: { module: shaderModule, entryPoint: vsEntry },
            fragment: { module: shaderModule, entryPoint: fsEntry, targets: [{ format }] },
            primitive: { topology: 'triangle-list' },
        });
    }

    const bindGroup = device.createBindGroup({
        layout: pipeline.getBindGroupLayout(0),
        entries: [{ binding: 0, resource: { buffer: uniformBuffer } }],
    });

    active = {
        canvas, context, device, pipeline, bindGroup, uniformBuffer,
        warpX, warpY, dotNetRef,
        renderMode: mode,
        cubeVB, cubeIB,
        depthTexture: null,
        cameraYaw:      prevCamera ? prevCamera.yaw      : 0.6,
        cameraPitch:    prevCamera ? prevCamera.pitch    : 0.3,
        cameraDistance: prevCamera ? prevCamera.distance : 4.0,
        startTimeMs: performance.now(),
        lastTime: 0,
        running: true,
        animFrameId: null,
    };
    attachCameraInput();
    scheduleFrame();
};
