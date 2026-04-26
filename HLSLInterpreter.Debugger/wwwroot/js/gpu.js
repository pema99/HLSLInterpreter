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

const PREAMBLE = `
cbuffer DebuggerGlobals : register(b0) {
    float2 _WarpSize;
    float2 _Resolution;
    float _Time;
};

struct DbgVSOut { float4 pos : SV_Position; };

[shader("vertex")]
DbgVSOut dbgVertex(uint vid : SV_VertexID) {
    float2 p = float2(float((vid << 1u) & 2u), float(vid & 2u));
    DbgVSOut o;
    o.pos = float4(p * 2.0 - 1.0, 0.0, 1.0);
    return o;
}
`;

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

async function compileToWgsl(hlslSource, fragEntryName) {
    const [{ slang, globalSession, wgslTarget }, testPreamble] =
        await Promise.all([getSlangGlobal(), getTestPreamble()]);

    const session = globalSession.createSession(wgslTarget);
    if (!session) throw new Error('Slang: createSession(WGSL) failed: ' + (slang.getLastError()?.message || ''));

    const fullSource = testPreamble + '\n' + PREAMBLE + '\n' + hlslSource;
    let userModule = null, vs = null, fs = null, composite = null, linked = null;
    try {
        userModule = session.loadModuleFromSource(fullSource, 'user', 'user.slang');
        if (!userModule) {
            const e = slang.getLastError();
            throw new Error('Slang compile error:\n' + (e?.message || 'unknown'));
        }
        vs = userModule.findAndCheckEntryPoint('dbgVertex', SLANG_STAGE_VERTEX);
        if (!vs) throw new Error("Slang: vertex entry 'dbgVertex' not found: " + (slang.getLastError()?.message || ''));
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

function drawFrame(r, now) {
    const prevW = r.canvas.width, prevH = r.canvas.height;
    fitCanvas(r.canvas);
    if ((r.canvas.width !== prevW || r.canvas.height !== prevH)
            && typeof window.dbgSetViewportImageSize === 'function') {
        window.dbgSetViewportImageSize('image-container', r.canvas.width, r.canvas.height);
    }

    const t = (now - r.startTimeMs) / 1000;
    r.lastTime = t;

    // Layout matches the cbuffer, padded to 32 bytes for WebGPU.
    const u = new Float32Array(8);
    u[0] = r.warpX;
    u[1] = r.warpY;
    u[2] = r.canvas.width;
    u[3] = r.canvas.height;
    u[4] = t;
    r.device.queue.writeBuffer(r.uniformBuffer, 0, u);

    let view;
    try { view = r.context.getCurrentTexture().createView(); }
    catch (e) { return false; }

    const enc = r.device.createCommandEncoder();
    const pass = enc.beginRenderPass({
        colorAttachments: [{
            view,
            clearValue: { r: 0, g: 0, b: 0, a: 1 },
            loadOp: 'clear',
            storeOp: 'store',
        }],
    });
    pass.setPipeline(r.pipeline);
    pass.setBindGroup(0, r.bindGroup);
    pass.draw(3);
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

window.gpuRender = async function (canvasId, hlslSource, entryPoint, warpX, warpY, dotNetRef) {
    if (!('gpu' in navigator)) throw new Error('WebGPU is not supported in this browser.');

    const canvas = document.getElementById(canvasId);
    if (!canvas) throw new Error('Canvas not found: ' + canvasId);

    window.gpuStop();

    const wgsl = await compileToWgsl(hlslSource, entryPoint);
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
        size: 32,
        usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST,
    });

    const pipeline = device.createRenderPipeline({
        layout: 'auto',
        vertex: { module: shaderModule, entryPoint: vsEntry },
        fragment: { module: shaderModule, entryPoint: fsEntry, targets: [{ format }] },
        primitive: { topology: 'triangle-list' },
    });

    const bindGroup = device.createBindGroup({
        layout: pipeline.getBindGroupLayout(0),
        entries: [{ binding: 0, resource: { buffer: uniformBuffer } }],
    });

    active = {
        canvas, context, device, pipeline, bindGroup, uniformBuffer,
        warpX, warpY, dotNetRef,
        startTimeMs: performance.now(),
        lastTime: 0,
        running: true,
        animFrameId: null,
    };
    scheduleFrame();
};
