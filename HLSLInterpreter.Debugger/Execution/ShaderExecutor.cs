using HLSL;
using UnityShaderParser.HLSL;
using HLSLInterpreter.Debugger.Utils;

namespace HLSLInterpreter.Debugger.Execution;

public readonly record struct StatementEvent(HLSLSyntaxNode Node, HLSLRunner Runner, int OutputLength);

public sealed class ExecutionOptions
{
    public static readonly ExecutionOptions None = new();

    public Action<StatementEvent> BeforeStatement { get; init; }
    public Action<StatementEvent> AfterStatement { get; init; }

    // Attach the hooks before the program is loaded so global initializers are
    // observed. Debug runs need this, plain runs and metrics do not.
    public bool ObserveProgramLoad { get; init; }

    // Redirect Console.Out for this run?
    public bool CaptureConsole { get; init; } = true;
}

public sealed record RunOutcome(
    HLSLValue Result,
    string Output,
    bool HasError,
    string ErrorMessage,
    Exception Exception)
{
    public static RunOutcome Success(HLSLValue result, string output) =>
        new(result, output, false, null, null);

    public static RunOutcome Failure(string output, string message, Exception exception) =>
        new(null, output, true, message, exception);
}

// Runs one shader invocation on the CPU interpreter and returns a RunOutcome
public sealed class ShaderExecutor
{
    public RunOutcome Execute(
        HLSLRunner runner,
        ShaderProgram program,
        ShaderInvocation invocation,
        ExecutionOptions options)
    {
        options ??= ExecutionOptions.None;
        ConsoleCapture capture = options.CaptureConsole ? new ConsoleCapture() : null;

        void AttachHooks()
        {
            runner.DebugHookBeforeStatement = options.BeforeStatement is { } before
                ? node => before(new StatementEvent(node, runner, capture?.Length ?? 0))
                : null;
            runner.DebugHookAfterStatement = options.AfterStatement is { } after
                ? node => after(new StatementEvent(node, runner, capture?.Length ?? 0))
                : null;
        }

        try
        {
            runner.Reset();
            runner.SetWarpSize(Math.Max(1, invocation.WarpX), Math.Max(1, invocation.WarpY));
            invocation.SetUniforms(runner);

            if (options.ObserveProgramLoad) AttachHooks();
            var errors = program.LoadInto(runner);
            if (errors.Count > 0)
            {
                string message = string.Join("\n", errors.Select(
                    d => $"Line {d.Location.Line}, col {d.Location.Column}: {d.Text}"));
                return RunOutcome.Failure(capture?.ToString() ?? "", message, null);
            }
            if (!options.ObserveProgramLoad) AttachHooks();

            var result = invocation.Execute(runner);
            return RunOutcome.Success(result, capture?.ToString() ?? "");
        }
        catch (Exception ex)
        {
            return RunOutcome.Failure(capture?.ToString() ?? "", ex.Message, ex);
        }
        finally
        {
            runner.DebugHookBeforeStatement = null;
            runner.DebugHookAfterStatement = null;
            capture?.Dispose();
        }
    }
}
