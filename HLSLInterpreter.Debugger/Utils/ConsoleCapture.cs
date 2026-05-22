namespace HLSLInterpreter.Debugger.Utils;

public sealed class ConsoleCapture : IDisposable
{
    private readonly TextWriter _previous;
    private readonly StringWriter _writer;

    public ConsoleCapture()
    {
        _writer = new StringWriter();
        _previous = Console.Out;
        Console.SetOut(TextWriter.Synchronized(_writer));
    }

    public int Length => _writer.GetStringBuilder().Length;

    public override string ToString() => _writer.ToString();

    public void Dispose() => Console.SetOut(_previous);
}
