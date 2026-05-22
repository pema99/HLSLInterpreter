// Camera state

const CAMERA_FOV_Y = 60 * Math.PI / 180;
let cameraYaw = 0.6;
let cameraPitch = 0.3;
let cameraDistance = 2.5;

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

// Right-handed perspective with depth [0,1]. Camera looks down -Z.
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

export function gpuView() {
    const cp = Math.cos(cameraPitch), sp = Math.sin(cameraPitch);
    const cy = Math.cos(cameraYaw),   sy = Math.sin(cameraYaw);
    const ex = sy * cp * cameraDistance;
    const ey = sp * cameraDistance;
    const ez = cy * cp * cameraDistance;
    return matLookAt(ex, ey, ez, 0, 0, 0, 0, 1, 0);
}

export function gpuProjection(canvasW, canvasH) {
    const aspect = Math.max(1e-4, canvasW / Math.max(1, canvasH));
    return matPerspective(CAMERA_FOV_Y, aspect, 0.1, 100);
}

export function gpuViewProjection(canvasW, canvasH) {
    return matMul(gpuProjection(canvasW, canvasH), gpuView());
}

export function gpuPickRay(imgX, imgY, imgW, imgH) {
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
}

export function rotateCamera(dx, dy) {
    cameraYaw -= dx * 0.01;
    cameraPitch = Math.max(-1.4, Math.min(1.4, cameraPitch + dy * 0.01));
}

export function zoomCamera(deltaY) {
    const k = Math.exp(deltaY * 0.0015);
    cameraDistance = Math.max(0.5, Math.min(100, cameraDistance * k));
}
