using HLSL;

namespace HLSLInterpreter.Debugger.Core;

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
