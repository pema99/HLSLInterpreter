using HLSLInterpreter.Debugger.Core;

namespace HLSLInterpreter.Debugger.State;

public partial class AppStore
{
    public void BeginRun() =>
        UpdateRun(r => r with
        {
            Status = RunStatus.Running,
            Backend = RunBackend.Cpu,
            Error = null,
            Output = "",
            Image = null,
            Metrics = null,
            CapturedFrame = null,
            ViewMode = DebugViewMode.Color,
        });

    public void FinishRun() => UpdateRun(r => r with { Status = RunStatus.Idle });

    public void SetRunStatus(RunStatus status) => UpdateRun(r => r with { Status = status });

    public void SetRunBackend(RunBackend backend) => UpdateRun(r => r with { Backend = backend });

    public void SetRunImage(ShaderImage image) => UpdateRun(r => r with { Image = image });

    public void SetRunMetrics(ExecutionMetrics metrics) => UpdateRun(r => r with { Metrics = metrics });

    public void SetRunOutput(string output) => UpdateRun(r => r with { Output = output ?? "" });

    public void SetRunError(string message, Exception exception) =>
        UpdateRun(r => r with { Error = new RunError(message, exception) });

    public void SetViewMode(DebugViewMode mode) => UpdateRun(r => r with { ViewMode = mode });

    public void SetGpuPaused(bool paused) => UpdateRun(r => r with { GpuPaused = paused });

    public void SetCapturedFrame(FrameCapture capture) => UpdateRun(r => r with { CapturedFrame = capture });

    public void SetGpuPreviewEnabled(bool enabled) => UpdateRun(r => r with { GpuPreviewEnabled = enabled });

    private void UpdateRun(Func<RunState, RunState> update) =>
        Update(s => s with { Run = update(s.Run) });
}
