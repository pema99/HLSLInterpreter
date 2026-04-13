using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityShaderParser.HLSL;
using UnityShaderParser.HLSL.PreProcessor;
using UnityShaderParser.Test;
using UnityShaderParser.Common;

namespace HLSLInterpreter.Tests
{
    [TestFixture]
    public class HLSLFileTests
    {
        private static string ShadersDirectory => Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "Shaders");

        // Parsed nodes are immutable and safe to reuse across runner instances.
        // Caching avoids re-parsing the same file for every individual test case.
        private static readonly Dictionary<string, List<HLSLSyntaxNode>> _nodeCache = new();
        private static HLSLRunner LoadFile(string filePath)
        {
            if (!_nodeCache.TryGetValue(filePath, out var nodes))
            {
                nodes = ShaderParser.ParseTopLevelDeclarations(File.ReadAllText(filePath));
                _nodeCache[filePath] = nodes;
            }
            var runner = new HLSLRunner();
            runner.ProcessCode(nodes);
            return runner;
        }

        public static IEnumerable<TestCaseData> DiscoverTests()
        {
            foreach (string file in Directory.GetFiles(ShadersDirectory, "*.hlsl").OrderBy(f => f))
            {
                string fileName = Path.GetFileNameWithoutExtension(file);
                HLSLRunner.TestRun[] tests = LoadFile(file).DiscoverTests();
                foreach (var test in tests)
                {
                    var data = new TestCaseData(file, test).SetName($"{fileName}.{test.TestName}");
                    if (!string.IsNullOrEmpty(test.Description))
                        data = data.SetDescription(test.Description);
                    if (!string.IsNullOrEmpty(test.Category))
                        data = data.SetDescription(test.Category);
                    yield return data;
                }
            }
        }

        [TestCaseSource(nameof(DiscoverTests))]
        public void RunTest(string filePath, HLSLRunner.TestRun testRun)
        {
            HLSLRunner.TestResult result = LoadFile(filePath).RunTests(new[] { testRun }).Single();

            if (result.Status == HLSLRunner.TestStatus.Ignored)
            {
                Assert.Ignore(result.Message);
            }
            else if (result.Status == HLSLRunner.TestStatus.Fail)
            {
                string message = string.IsNullOrEmpty(result.Message)
                    ? $"Test failed: {result.TestName}"
                    : result.Message;

                if (!string.IsNullOrEmpty(result.Log))
                    message += $"\nLog:\n{result.Log}";

                Assert.Fail(message);
            }
        }
    }
}
