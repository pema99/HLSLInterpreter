namespace HLSLInterpreter.Debugger.Core;

/// <summary>
/// DTO returned to JS for the editor's hover popup.
/// </summary>
public sealed class HoverInfo
{
    public string Value { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public byte[]? Rgba { get; set; }
    public int InspectedX { get; set; }
    public int InspectedY { get; set; }
}
