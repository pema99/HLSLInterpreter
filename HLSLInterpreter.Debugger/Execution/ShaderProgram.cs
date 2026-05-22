using HLSL;
using UnityShaderParser.Common;
using UnityShaderParser.HLSL;

namespace HLSLInterpreter.Debugger.Execution;

// A shader either parsed from source or supplied as pre-parsed nodes
public sealed class ShaderProgram
{
    private readonly string _source;
    private readonly HLSLParserConfig _config;
    private readonly IReadOnlyList<HLSLSyntaxNode> _nodes;

    private ShaderProgram(string source, HLSLParserConfig config, IReadOnlyList<HLSLSyntaxNode> nodes)
    {
        _source = source;
        _config = config;
        _nodes = nodes;
    }

    public static ShaderProgram FromSource(string source, HLSLParserConfig config) =>
        new(source, config, null);

    public static ShaderProgram FromParsedNodes(IReadOnlyList<HLSLSyntaxNode> nodes) =>
        new(null, null, nodes);

    public static IReadOnlyList<HLSLSyntaxNode> Parse(string source, HLSLParserConfig config) =>
        ShaderParser.ParseTopLevelDeclarations(source, config);

    public IReadOnlyList<Diagnostic> LoadInto(HLSLRunner runner)
    {
        if (_nodes != null)
        {
            runner.ProcessCode(_nodes);
            return Array.Empty<Diagnostic>();
        }
        runner.ProcessCode(_source, _config, out var diagnostics, out _);
        return diagnostics.Where(d => (d.Kind & DiagnosticFlags.OnlyErrors) != 0).ToList();
    }
}
