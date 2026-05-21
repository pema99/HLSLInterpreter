using Microsoft.JSInterop;

namespace HLSLInterpreter.Debugger.Interop;

// Typed wrapper over the image canvas in imagestate.js. The painted pixels and
// the overlay state (warp, modes, click handlers, ...) are pushed separately:
// pixels are large and change rarely, the overlay state is one small object
// pushed after every message.
public sealed class CanvasInterop
{
    private const string ModulePath = "./_content/HLSLInterpreter.Debugger/js/imagestate.js";

    private readonly IJSRuntime _js;
    private IJSObjectReference _module;

    public CanvasInterop(IJSRuntime js) => _js = js;

    private async ValueTask<IJSObjectReference> Module() =>
        _module ??= await _js.InvokeAsync<IJSObjectReference>("import", ModulePath);

    public async ValueTask SetPixels(byte[] pixels, int width, int height) =>
        await (await Module()).InvokeVoidAsync("imgSetPixels", pixels, width, height);

    public async ValueTask SetPixelsRect(byte[] pixels, int x, int y, int width, int height) =>
        await (await Module()).InvokeVoidAsync("imgSetPixelsRect", pixels, x, y, width, height);

    public async ValueTask AllocPixels(int width, int height) =>
        await (await Module()).InvokeVoidAsync("imgAllocPixels", width, height);

    public async ValueTask<int[]> GetCpuCanvasSize() =>
        await (await Module()).InvokeAsync<int[]>("cpuCanvasSize");

    // The whole canvas overlay projection, pushed in one call.
    public async ValueTask SetState(object state) =>
        await (await Module()).InvokeVoidAsync("imgSetState", state);

    public async ValueTask SetMeshData(float[] positions, uint[] indices) =>
        await (await Module()).InvokeVoidAsync("imgSetMeshData", positions, indices);
}
