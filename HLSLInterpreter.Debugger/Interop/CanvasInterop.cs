using Microsoft.JSInterop;

namespace HLSLInterpreter.Debugger.Interop;

// Typed wrapper over the image canvas in viewport.js. Pixels are painted
// straight onto the <canvas>; the overlay projection is applied per container
// by CanvasView.
public sealed class CanvasInterop
{
    private const string ModulePath = "./_content/HLSLInterpreter.Debugger/js/viewport.js";

    private readonly IJSRuntime _js;
    private IJSObjectReference _module;

    public CanvasInterop(IJSRuntime js) => _js = js;

    private async ValueTask<IJSObjectReference> Module() =>
        _module ??= await _js.InvokeAsync<IJSObjectReference>("import", ModulePath);

    public async ValueTask SetPixels(byte[] pixels, int width, int height) =>
        await (await Module()).InvokeVoidAsync("setPixels", pixels, width, height);

    public async ValueTask SetPixelsRect(byte[] pixels, int x, int y, int width, int height) =>
        await (await Module()).InvokeVoidAsync("setPixelsRect", pixels, x, y, width, height);

    public async ValueTask AllocPixels(int width, int height) =>
        await (await Module()).InvokeVoidAsync("allocPixels", width, height);

    public async ValueTask<int[]> GetCpuCanvasSize() =>
        await (await Module()).InvokeAsync<int[]>("cpuCanvasSize");

    public async ValueTask ApplyState(string containerId, object projection) =>
        await (await Module()).InvokeVoidAsync("applyCanvasState", containerId, projection);

    public async ValueTask SetMeshData(float[] positions, uint[] indices) =>
        await (await Module()).InvokeVoidAsync("setMeshData", positions, indices);
}
