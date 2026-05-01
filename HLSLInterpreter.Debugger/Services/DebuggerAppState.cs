using System.ComponentModel;
using System.Runtime.CompilerServices;
using HLSLInterpreter.Debugger.Core;

namespace HLSLInterpreter.Debugger.Services;

public enum ShaderRenderMode { Pixel, VertFrag }
public enum DebugTarget { Pixel, Vertex }

public sealed class DebuggerAppState : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private Mesh _currentMesh = Mesh.CreateCube();
    public Mesh CurrentMesh
    {
        get => _currentMesh;
        set => Set(ref _currentMesh, value);
    }

    private string _fragmentEntryPoint = "frag";
    public string FragmentEntryPoint
    {
        get => _fragmentEntryPoint;
        set => Set(ref _fragmentEntryPoint, value);
    }

    private string _vertexEntryPoint = "vert";
    public string VertexEntryPoint
    {
        get => _vertexEntryPoint;
        set => Set(ref _vertexEntryPoint, value);
    }

    private ShaderRenderMode _renderMode = ShaderRenderMode.Pixel;
    public ShaderRenderMode ShaderRenderMode
    {
        get => _renderMode;
        set => Set(ref _renderMode, value);
    }

    private int _warpX = 16;
    public int WarpX
    {
        get => _warpX;
        set
        {
            if (Set(ref _warpX, value)) ClampInspectedThread();
        }
    }

    private int _warpY = 16;
    public int WarpY
    {
        get => _warpY;
        set
        {
            if (Set(ref _warpY, value)) ClampInspectedThread();
        }
    }

    private int _groupOffsetX = 0;
    public int GroupOffsetX
    {
        get => _groupOffsetX;
        set => Set(ref _groupOffsetX, value);
    }

    private int _groupOffsetY = 0;
    public int GroupOffsetY
    {
        get => _groupOffsetY;
        set => Set(ref _groupOffsetY, value);
    }

    private DebugTarget _debugTarget = DebugTarget.Pixel;
    public DebugTarget DebugTarget
    {
        get => _debugTarget;
        set => Set(ref _debugTarget, value);
    }

    private int _debugVertexIndex = -1;
    public int DebugVertexIndex
    {
        get => _debugVertexIndex;
        set => Set(ref _debugVertexIndex, value);
    }

    private bool _gpuPreviewEnabled = false;
    public bool GpuPreviewEnabled
    {
        get => _gpuPreviewEnabled;
        set => Set(ref _gpuPreviewEnabled, value);
    }

    private int _inspectedThread = 0;
    public int InspectedThread
    {
        get => _inspectedThread;
        set => Set(ref _inspectedThread, Math.Clamp(value, 0, Math.Max(0, _warpX * _warpY - 1)));
    }

    private int _selectedFrame = 0;
    public int SelectedFrame
    {
        get => _selectedFrame;
        set => Set(ref _selectedFrame, Math.Max(0, value));
    }

    public PermalinkSettings ToPermalinkSettings() =>
        new(FragmentEntryPoint, WarpX, WarpY, GroupOffsetX, GroupOffsetY, GpuPreviewEnabled, ShaderRenderMode, VertexEntryPoint);

    public void ApplyFromUrl(string url)
    {
        var s = PermalinkCodec.ApplyToSettings(url, ToPermalinkSettings());
        FragmentEntryPoint = s.EntryPoint;
        WarpX = s.WarpX;
        WarpY = s.WarpY;
        GroupOffsetX = s.GroupOffsetX;
        GroupOffsetY = s.GroupOffsetY;
        GpuPreviewEnabled = s.GpuPreviewEnabled;
        ShaderRenderMode = s.ShaderRenderMode;
        VertexEntryPoint = s.VertexEntryPoint;
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    private void ClampInspectedThread()
    {
        int clamped = Math.Clamp(_inspectedThread, 0, Math.Max(0, _warpX * _warpY - 1));
        if (clamped != _inspectedThread)
        {
            _inspectedThread = clamped;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(InspectedThread)));
        }
    }
}
