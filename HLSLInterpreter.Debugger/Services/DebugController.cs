using System.ComponentModel;
using System.Runtime.CompilerServices;
using HLSL;
using HLSLInterpreter.Debugger.Core;
using UnityShaderParser.HLSL;

namespace HLSLInterpreter.Debugger.Services;

public sealed class DebugController : INotifyPropertyChanged
{
    private readonly DebuggerAppState _state;
    private readonly RunController _run;

    public DebugController(DebuggerAppState state, RunController run)
    {
        _state = state;
        _run = run;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private DebuggerSession? _session;
    public DebuggerSession? CurrentSession { get => _session; private set => Set(ref _session, value); }

    private bool _isDebugging;
    public bool IsDebugging { get => _isDebugging; private set => Set(ref _isDebugging, value); }

    private string _debugCode = "";
    public string DebugCode { get => _debugCode; private set => Set(ref _debugCode, value); }

    private int _debugTabIndex = -1;
    public int DebugTabIndex { get => _debugTabIndex; set => Set(ref _debugTabIndex, value); }

    private (int X, int Y)? _savedGroupOffsets;

    public HashSet<int> Breakpoints { get; } = new();

    public int CurrentDebugLine => CurrentSession?.CurrentLine ?? 0;
    public string CurrentDebugOutput => CurrentSession?.OutputThroughCurrentStep ?? "";

    public void ToggleBreakpoint(int line)
    {
        if (!Breakpoints.Add(line)) Breakpoints.Remove(line);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Breakpoints)));
    }

    public void SaveGroupOffsetsForRestore()
    {
        _savedGroupOffsets = (_state.GroupOffsetX, _state.GroupOffsetY);
    }

    public async Task<bool> StartSessionAsync(
        Func<Task<string>> getCode,
        Func<HLSLParserConfig> makeParserConfig)
    {
        _run.IsRunning = true;
        _run.HasError = false;
        _run.ErrorMessage = "";
        _run.LastException = null;
        _run.Output = null;

        await _run.SnapshotGpuIfNeededAsync();
        await _run.StopGpuAsync();
        CurrentSession = null;

        string code = await getCode();
        DebugCode = code;
        int wx = Math.Max(1, _state.WarpX);
        int wy = Math.Max(1, _state.WarpY);

        var newSession = DebuggerSession.Record(
            code, wx, wy, _state.GroupOffsetX, _state.GroupOffsetY, _state.EntryPoint,
            configureGlobals: _run.SetSharedGlobals,
            parserConfig: makeParserConfig());

        CurrentSession = newSession;
        _run.HasError = newSession.HasError;
        _run.ErrorMessage = newSession.ErrorMessage ?? "";
        _run.LastException = newSession.LastException;
        _run.Output = newSession.Output;
        if (!_run.HasError && newSession.Result != null)
            _run.TryExtractImage(newSession.Result, wx, wy);

        _run.IsRunning = false;

        if (_run.HasError && !(_run.LastException is HLSLRunner.TestFailException && newSession.Trace.Count > 0))
        {
            CurrentSession = null;
            return false;
        }

        // When failing on a test assertion, jump to the failing step.
        if (newSession.Trace.Count > 0 && _run.HasError)
            newSession.SeekToEnd();

        IsDebugging = true;
        return true;
    }

    public void ExitSession()
    {
        IsDebugging = false;
        DebugTabIndex = -1;
        CurrentSession = null;
        _run.GpuCaptured = null;
        _run.IsGpuMode = false;
        if (_savedGroupOffsets.HasValue)
        {
            _state.GroupOffsetX = _savedGroupOffsets.Value.X;
            _state.GroupOffsetY = _savedGroupOffsets.Value.Y;
            _savedGroupOffsets = null;
        }
    }

    public void StepForward()      => Navigate(s => s.StepForward());
    public void StepBack()         => Navigate(s => s.StepBack());
    public void StepOver()         => Navigate(s => s.StepOver());
    public void StepOut()          => Navigate(s => s.StepOut());
    public void StepOverBack()     => Navigate(s => s.StepOverBack());
    public void StepOutBack()      => Navigate(s => s.StepOutBack());
    public void ContinueToBreakpoint()     => Navigate(s => s.ContinueToBreakpoint(Breakpoints));
    public void ContinueToBreakpointBack() => Navigate(s => s.ContinueToBreakpointBack(Breakpoints));

    private void Navigate(Action<DebuggerSession> nav)
    {
        if (CurrentSession == null) return;
        nav(CurrentSession);
        _state.SelectedFrame = 0;
        // PropertyChanged for "CurrentSession" is a stand-in for "step index changed". The ref
        // didn't change, but the cursor inside it did, and listeners need to re-render.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentSession)));
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
