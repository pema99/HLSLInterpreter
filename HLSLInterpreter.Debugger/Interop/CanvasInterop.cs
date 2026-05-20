using Microsoft.JSInterop;

namespace HLSLInterpreter.Debugger.Interop;

// Typed wrapper over the image canvas state in imagestate.js. Nullable arguments
// map to JS null, which clears the corresponding overlay.
public interface ICanvasInterop
{
    ValueTask SetPixels(byte[] pixels, int width, int height);
    ValueTask SetPixelsRect(byte[] pixels, int x, int y, int width, int height);
    ValueTask AllocPixels(int width, int height);
    ValueTask<int[]> GetCpuCanvasSize();
    ValueTask SetWarp(int warpX, int warpY);
    ValueTask SetRegularMode(string mode);
    ValueTask SetDebugMode(string mode);
    ValueTask SetDebugPixel(int? x, int? y);
    ValueTask SetThreadStates(int[] states);
    ValueTask SetCpuClickHandler(int? warpX, int? warpY);
    ValueTask SetDebugClickHandler(bool active);
    ValueTask SetPickMode(string mode);
    ValueTask SetMeshData(float[] positions, uint[] indices);
}

public sealed class CanvasInterop : ICanvasInterop
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

    public async ValueTask SetWarp(int warpX, int warpY) =>
        await (await Module()).InvokeVoidAsync("imgSetWarp", warpX, warpY);

    public async ValueTask SetRegularMode(string mode) =>
        await (await Module()).InvokeVoidAsync("imgSetRegularMode", mode);

    public async ValueTask SetDebugMode(string mode) =>
        await (await Module()).InvokeVoidAsync("imgSetDebugMode", mode);

    public async ValueTask SetDebugPixel(int? x, int? y) =>
        await (await Module()).InvokeVoidAsync("imgSetDebugPixel", x, y);

    public async ValueTask SetThreadStates(int[] states) =>
        await (await Module()).InvokeVoidAsync("imgSetThreadStates", states);

    public async ValueTask SetCpuClickHandler(int? warpX, int? warpY) =>
        await (await Module()).InvokeVoidAsync("imgSetCpuClickHandler", warpX, warpY);

    public async ValueTask SetDebugClickHandler(bool active) =>
        await (await Module()).InvokeVoidAsync("imgSetDebugClickHandler", active);

    public async ValueTask SetPickMode(string mode) =>
        await (await Module()).InvokeVoidAsync("imgSetPickMode", mode);

    public async ValueTask SetMeshData(float[] positions, uint[] indices) =>
        await (await Module()).InvokeVoidAsync("imgSetMeshData", positions, indices);
}
