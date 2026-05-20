using HLSLInterpreter.Debugger.Core;
using Microsoft.JSInterop;

namespace HLSLInterpreter.Debugger.Interop;

public sealed record GpuRenderRequest(
    string CanvasId,
    string Source,
    string FragmentEntryPoint,
    int WarpX,
    int WarpY,
    object DotNetRef,
    string Mode,
    string VertexEntryPoint,
    IReadOnlyList<VertexInput> VertexInputs,
    float[] MeshVertices,
    uint[] MeshIndices,
    float Time,
    IReadOnlyList<TextureBinding> Textures,
    IReadOnlyList<SamplerBinding> Samplers);

// Typed wrapper over the WebGPU preview loop in gpu.js.
public interface IGpuInterop
{
    ValueTask<bool> IsAvailable();
    ValueTask Render(GpuRenderRequest request);
    ValueTask Stop();
    ValueTask Pause();
    ValueTask Resume();
    ValueTask Restart();
    ValueTask<float[]> Snapshot();
    ValueTask<float[]> View();
    ValueTask<float[]> Projection(int canvasW, int canvasH);
    ValueTask<float[]> Mouse();
}

public sealed class GpuInterop : IGpuInterop
{
    private readonly IJSRuntime _js;

    public GpuInterop(IJSRuntime js) => _js = js;

    public async ValueTask<bool> IsAvailable()
    {
        try { return await _js.InvokeAsync<bool>("gpuIsAvailable"); }
        catch { return false; }
    }

    public ValueTask Render(GpuRenderRequest r) =>
        _js.InvokeVoidAsync("gpuRender", r.CanvasId, r.Source, r.FragmentEntryPoint,
            r.WarpX, r.WarpY, r.DotNetRef, r.Mode, r.VertexEntryPoint, r.VertexInputs,
            r.MeshVertices, r.MeshIndices, r.Time, r.Textures, r.Samplers);

    public ValueTask Stop() => _js.InvokeVoidAsync("gpuStop");

    public ValueTask Pause() => _js.InvokeVoidAsync("gpuPause");

    public ValueTask Resume() => _js.InvokeVoidAsync("gpuResume");

    public ValueTask Restart() => _js.InvokeVoidAsync("gpuRestart");

    public ValueTask<float[]> Snapshot() => _js.InvokeAsync<float[]>("gpuSnapshot");

    public ValueTask<float[]> View() => _js.InvokeAsync<float[]>("gpuView");

    public ValueTask<float[]> Projection(int canvasW, int canvasH) =>
        _js.InvokeAsync<float[]>("gpuProjection", canvasW, canvasH);

    public ValueTask<float[]> Mouse() => _js.InvokeAsync<float[]>("gpuMouse");
}
