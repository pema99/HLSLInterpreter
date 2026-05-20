using HLSLInterpreter.Debugger.Core;

namespace HLSLInterpreter.Debugger.State;

public partial class AppStore
{
    public void StartDebug(ExecutionTrace trace, int documentId, string code, int stepIndex) =>
        Update(s => s with { Debug = s.Debug with
        {
            IsActive = true,
            Trace = trace,
            StepIndex = stepIndex,
            DebugDocumentId = documentId,
            DebugCode = code,
            SelectedFrame = 0,
        } });

    public void ExitDebug() =>
        Update(s => s with { Debug = s.Debug with
        {
            IsActive = false,
            Trace = null,
            StepIndex = 0,
            DebugDocumentId = -1,
            DebugVertexIndex = -1,
            SelectedFrame = 0,
        } });

    public void StepForward()  => NavigateTrace(TraceNavigator.Forward);
    public void StepBack()     => NavigateTrace(TraceNavigator.Back);
    public void StepOver()     => NavigateTrace(TraceNavigator.Over);
    public void StepOut()      => NavigateTrace(TraceNavigator.Out);
    public void StepOverBack() => NavigateTrace(TraceNavigator.OverBack);
    public void StepOutBack()  => NavigateTrace(TraceNavigator.OutBack);

    public void ContinueToBreakpoint() =>
        NavigateTrace((t, i) => TraceNavigator.ToBreakpoint(t, i, State.Debug.Breakpoints));

    public void ContinueToBreakpointBack() =>
        NavigateTrace((t, i) => TraceNavigator.ToBreakpointBack(t, i, State.Debug.Breakpoints));

    public void ToggleBreakpoint(int line) =>
        Update(s =>
        {
            var breakpoints = new HashSet<int>(s.Debug.Breakpoints);
            if (!breakpoints.Add(line)) breakpoints.Remove(line);
            return s with { Debug = s.Debug with { Breakpoints = breakpoints } };
        });

    public void SetSelectedFrame(int frame) =>
        Update(s => s with { Debug = s.Debug with { SelectedFrame = Math.Max(0, frame) } });

    public void SetInspectedThread(int thread) =>
        Update(s => WithInspectedThread(s, thread));

    public void SetBottomMode(DebugBottomMode mode) =>
        Update(s => s with { Debug = s.Debug with { BottomMode = mode } });

    public void SetDebugVertexIndex(int index) =>
        Update(s => s with { Debug = s.Debug with { DebugVertexIndex = index } });

    private void NavigateTrace(Func<ExecutionTrace, int, int> navigate) =>
        Update(s => s.Debug.Trace == null ? s
            : s with { Debug = s.Debug with
            {
                StepIndex = navigate(s.Debug.Trace, s.Debug.StepIndex),
                SelectedFrame = 0,
            } });

    private static AppState WithInspectedThread(AppState s, int thread)
    {
        var config = s.Editor.ActiveDocument?.Config;
        int max = config == null ? 0 : Math.Max(0, config.WarpX * config.WarpY - 1);
        return s with { Debug = s.Debug with { InspectedThread = Math.Clamp(thread, 0, max) } };
    }

    private static AppState WithClampedInspectedThread(AppState s) =>
        WithInspectedThread(s, s.Debug.InspectedThread);
}
