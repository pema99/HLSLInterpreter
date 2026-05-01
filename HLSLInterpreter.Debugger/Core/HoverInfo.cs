namespace HLSLInterpreter.Debugger.Core;

public sealed class HoverInfo
{
    public string Value { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public byte[]? Rgba { get; set; }
    public int InspectedX { get; set; }
    public int InspectedY { get; set; }
    public string[]? PerThreadValues { get; set; }
}
