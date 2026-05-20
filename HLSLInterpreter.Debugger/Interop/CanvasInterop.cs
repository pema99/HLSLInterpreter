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
    ValueTask<string> RgbaToDataUrl(byte[] rgba, int width, int height,
        int inspectedX, int inspectedY, double sizeScale);
}

public sealed class CanvasInterop : ICanvasInterop
{
    private readonly IJSRuntime _js;

    public CanvasInterop(IJSRuntime js) => _js = js;

    public ValueTask SetPixels(byte[] pixels, int width, int height) =>
        _js.InvokeVoidAsync("imgSetPixels", pixels, width, height);

    public ValueTask SetPixelsRect(byte[] pixels, int x, int y, int width, int height) =>
        _js.InvokeVoidAsync("imgSetPixelsRect", pixels, x, y, width, height);

    public ValueTask AllocPixels(int width, int height) =>
        _js.InvokeVoidAsync("imgAllocPixels", width, height);

    public ValueTask<int[]> GetCpuCanvasSize() => _js.InvokeAsync<int[]>("cpuCanvasSize");

    public ValueTask SetWarp(int warpX, int warpY) =>
        _js.InvokeVoidAsync("imgSetWarp", warpX, warpY);

    public ValueTask SetRegularMode(string mode) => _js.InvokeVoidAsync("imgSetRegularMode", mode);

    public ValueTask SetDebugMode(string mode) => _js.InvokeVoidAsync("imgSetDebugMode", mode);

    public ValueTask SetDebugPixel(int? x, int? y) =>
        _js.InvokeVoidAsync("imgSetDebugPixel", x, y);

    public ValueTask SetThreadStates(int[] states) =>
        _js.InvokeVoidAsync("imgSetThreadStates", states);

    public ValueTask SetCpuClickHandler(int? warpX, int? warpY) =>
        _js.InvokeVoidAsync("imgSetCpuClickHandler", warpX, warpY);

    public ValueTask SetDebugClickHandler(bool active) =>
        _js.InvokeVoidAsync("imgSetDebugClickHandler", active);

    public ValueTask SetPickMode(string mode) => _js.InvokeVoidAsync("imgSetPickMode", mode);

    public ValueTask SetMeshData(float[] positions, uint[] indices) =>
        _js.InvokeVoidAsync("imgSetMeshData", positions, indices);

    public ValueTask<string> RgbaToDataUrl(byte[] rgba, int width, int height,
        int inspectedX, int inspectedY, double sizeScale) =>
        _js.InvokeAsync<string>("rgbaToDataUrl", rgba, width, height, inspectedX, inspectedY, sizeScale);
}
