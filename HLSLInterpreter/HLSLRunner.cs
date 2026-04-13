using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityShaderParser.Common;
using UnityShaderParser.HLSL;

namespace UnityShaderParser.Test
{
    public class HLSLRunner
    {
        [Serializable]
        private class TestFailException : Exception
        {
            public TestFailException() { }
            public TestFailException(string message) : base(message) { }
            public TestFailException(string message, Exception innerException) : base(message, innerException) { }
        }

        [Serializable]
        private class TestPassException : Exception
        {
            public TestPassException() { }
            public TestPassException(string message) : base(message) { }
            public TestPassException(string message, Exception innerException) : base(message, innerException) { }
        }

        public enum TestStatus { Pass, Fail, Ignored }

        public struct TestRun
        {
            public string TestName;
            public string FunctionName;
            public bool UsesCustomWarpSize;
            public int WarpSizeX;
            public int WarpSizeY;
            public Func<List<HLSLValue>> GetInputs;
        }

        public struct TestResult
        {
            public string TestName;
            public TestStatus Status;
            public string Message;
            public string Log;
        }

        protected HLSLInterpreter interpreter;

        public HLSLRunner(int defaultThreadsX = 2, int defaultThreadsY = 2)
        {
            interpreter = new HLSLInterpreter(defaultThreadsX, defaultThreadsY);

            // Add magic callbacks for test running

            // Check that every active thread and every component of 'val' is truthy.
            void CheckAllTrue(HLSLExecutionState state, HLSLValue val, string failMsg)
            {
                if (val is ScalarValue sv)
                {
                    for (int i = 0; i < sv.ThreadCount; i++)
                        if (state.IsThreadActive(i) && !Convert.ToBoolean(sv.Value.Get(i)))
                            throw new TestFailException(failMsg);
                }
                else if (val is VectorValue vv)
                {
                    for (int i = 0; i < vv.ThreadCount; i++)
                        if (state.IsThreadActive(i))
                            foreach (var b in vv.Values.Get(i))
                                if (!Convert.ToBoolean(b))
                                    throw new TestFailException(failMsg);
                }
                else if (val is MatrixValue mv)
                {
                    for (int i = 0; i < mv.ThreadCount; i++)
                        if (state.IsThreadActive(i))
                            foreach (var b in mv.Values.Get(i))
                                if (!Convert.ToBoolean(b))
                                    throw new TestFailException(failMsg);
                }
                else
                {
                    throw new TestFailException(failMsg);
                }
            }

            ScalarValue Assert(HLSLExecutionState state, ExpressionNode[] args)
            {
                if (args.Length > 0)
                {
                    string message = null;
                    if (args.Length > 1)
                        message = (interpreter.EvaluateExpression(args[1]) as ScalarValue).Value.Get(0) as string;

                    HLSLValue val = interpreter.EvaluateExpression(args[0]);
                    CheckAllTrue(state, val, message ?? $"Assertion failed: {args[0].GetPrettyPrintedCode()}");
                }
                return ScalarValue.Null;
            }

            interpreter.AddCallback("ASSERT", (state, args) =>
            {
                return Assert(state, args);
            });

            interpreter.AddCallback("ASSERT_MSG", (state, args) =>
            {
                return Assert(state, args);
            });

            interpreter.AddCallback("ASSERT_EQUAL", (state, args) =>
            {
                if (args.Length >= 2)
                {
                    var lhsVal = interpreter.EvaluateExpression(args[0]);
                    var rhsVal = interpreter.EvaluateExpression(args[1]);

                    if (lhsVal is not NumericValue a || rhsVal is not NumericValue b)
                        throw new TestFailException("ASSERT_EQUAL: arguments must be numeric values.");

                    string failMsg = $"ASSERT_EQUAL failed: {args[0].GetPrettyPrintedCode()} [{a}] != {args[1].GetPrettyPrintedCode()} [{b}]";
                    CheckAllTrue(state, a == b, failMsg);
                }
                return ScalarValue.Null;
            });

            interpreter.AddCallback("ASSERT_NEAR", (state, args) =>
            {
                if (args.Length >= 3)
                {
                    var lhsVal = interpreter.EvaluateExpression(args[0]);
                    var rhsVal = interpreter.EvaluateExpression(args[1]);
                    var epsVal = interpreter.EvaluateExpression(args[2]);

                    if (lhsVal is not NumericValue a || rhsVal is not NumericValue b || epsVal is not NumericValue eps)
                        throw new TestFailException("ASSERT_NEAR: arguments must be numeric values.");

                    var diff = HLSLIntrinsics.Abs(a - b);
                    string failMsg = $"ASSERT_NEAR failed: |{args[0].GetPrettyPrintedCode()} - {args[1].GetPrettyPrintedCode()}| = {diff} > {eps}";
                    CheckAllTrue(state, diff <= eps, failMsg);
                }
                return ScalarValue.Null;
            });

            interpreter.AddCallback("ASSERT_UNIFORM", (state, args) =>
            {
                if (args.Length > 0 && state.IsAnyThreadActive())
                {
                    var val = interpreter.EvaluateExpression(args[0]);
                    if (val.IsVarying)
                        throw new TestFailException($"ASSERT_UNIFORM failed: '{args[0].GetPrettyPrintedCode()}' is varying across threads.");
                }
                return ScalarValue.Null;
            });

            interpreter.AddCallback("ASSERT_VARYING", (state, args) =>
            {
                if (args.Length > 0 && state.IsAnyThreadActive())
                {
                    var val = interpreter.EvaluateExpression(args[0]);
                    if (val.IsUniform)
                        throw new TestFailException($"ASSERT_VARYING failed: '{args[0].GetPrettyPrintedCode()}' is uniform across threads.");
                }
                return ScalarValue.Null;
            });

            interpreter.AddCallback("PASS_TEST", (state, args) =>
            {
                if (state.IsAnyThreadActive())
                {
                    if (args.Length > 0)
                        throw new TestPassException(interpreter.EvaluateExpression(args[0]).ToString());
                    else
                        throw new TestPassException();
                }
                return ScalarValue.Null;
            });

            interpreter.AddCallback("FAIL_TEST", (state, args) =>
            {
                if (state.IsAnyThreadActive())
                {
                    if (args.Length > 0)
                        throw new TestFailException(interpreter.EvaluateExpression(args[0]).ToString());
                    else
                        throw new TestFailException();
                }
                return ScalarValue.Null;
            });

            interpreter.AddCallback("PRINTF", (state, args) =>
            {
                HLSLIntrinsics.Printf(state, args.Select(x => interpreter.EvaluateExpression(x)).ToArray());
                return ScalarValue.Null;
            });

            interpreter.AddCallback("MOCK_RESOURCE", (state, args) =>
            {
                if (args.Length != 2)
                    throw new Exception("MOCK_RESOURCE requires exactly 2 arguments: MOCK_RESOURCE(resource, MockStructType).");

                string resourceName = (args[0] as IdentifierExpressionNode)?.GetName()
                    ?? throw new Exception("First argument to MOCK_RESOURCE must be a resource variable name.");
                string mockStructName = (args[1] as IdentifierExpressionNode)?.GetName()
                    ?? throw new Exception("Second argument to MOCK_RESOURCE must be a mock struct type name.");

                var existing = interpreter.GetVariable(resourceName) as ResourceValue
                    ?? throw new Exception($"MOCK_RESOURCE: '{resourceName}' is not a resource variable.");

                var mock = interpreter.CreateMockResource(mockStructName, existing.Type, existing.TemplateArguments);
                interpreter.SetVariable(resourceName, mock);
                return ScalarValue.Null;
            });
        }

        private HLSLParserConfig AddTestRunnerDefine(HLSLParserConfig config)
        {
            var newConfig = new HLSLParserConfig()
            {
                PreProcessorMode = config.PreProcessorMode,
                BasePath = config.BasePath,
                FileName = config.FileName,
                IncludeResolver = config.IncludeResolver,
                Defines = new Dictionary<string, string>(config.Defines),
                ThrowExceptionOnError = config.ThrowExceptionOnError,
                DiagnosticFilter = config.DiagnosticFilter,
            };
            newConfig.Defines.Add("__HLSL_TEST_RUNNER__", "1");
            return newConfig;
        }

        private bool TryBuildMockResourceFactories(FunctionDefinitionNode func, out Func<HLSLValue>[] factories)
        {
            bool any = false;
            factories = new Func<HLSLValue>[func.Parameters.Count];

            for (int i = 0; i < func.Parameters.Count; i++)
            {
                var param = func.Parameters[i];
                foreach (var attr in param.Attributes)
                {
                    if (attr.Name.Identifier.ToLower() != "mockresource" || attr.Arguments.Count == 0)
                        continue;

                    string mockStructName = (attr.Arguments[0] as IdentifierExpressionNode)?.GetName();
                    if (mockStructName == null)
                        continue;

                    if (param.ParamType is not PredefinedObjectTypeNode resourceTypeNode)
                        continue;

                    any = true;
                    var capturedStructName = mockStructName;
                    var capturedKind = resourceTypeNode.Kind;
                    var capturedTemplateArgs = resourceTypeNode.TemplateArguments.ToArray();

                    factories[i] = () => interpreter.CreateMockResource(capturedStructName, capturedKind, capturedTemplateArgs);
                }

            }

            return any;
        }

        private bool IsIgnored(string functionName, List<HLSLValue> inputs, out string reason)
        {
            reason = null;
            var func = interpreter.GetFunction(functionName, inputs.ToArray());
            foreach (var attr in func.Attributes.Where(x => x.Name.Identifier.ToLower() == "ignore"))
            {
                if (attr.Arguments.Count > 0)
                    reason = interpreter.EvaluateExpression(attr.Arguments[0]).ToString();
                return true;
            }
            return false;
        }

        public void ProcessCode(string code) =>
            interpreter.VisitMany(ShaderParser.ParseTopLevelDeclarations(code, AddTestRunnerDefine(new HLSLParserConfig())));
        public void ProcessCode(string code, HLSLParserConfig config) =>
            interpreter.VisitMany(ShaderParser.ParseTopLevelDeclarations(code, AddTestRunnerDefine(config)));
        public void ProcessCode(string code, out List<Diagnostic> diagnostics, out List<string> pragmas) =>
            interpreter.VisitMany(ShaderParser.ParseTopLevelDeclarations(code, out diagnostics, out pragmas));
        public void ProcessCode(string code, HLSLParserConfig config, out List<Diagnostic> diagnostics, out List<string> pragmas) =>
            interpreter.VisitMany(ShaderParser.ParseTopLevelDeclarations(code, AddTestRunnerDefine(config), out diagnostics, out pragmas));
        public void ProcessCode(IEnumerable<HLSLSyntaxNode> nodes) =>
            interpreter.VisitMany(nodes);

        public void Reset() => interpreter.Reset();

        public void SetWarpSize(int threadsX, int threadsY) => interpreter.SetWarpSize(threadsX, threadsY);
        public void SetVariable(string name, HLSLValue value) => interpreter.SetVariable(name, value);
        public HLSLValue GetVariable(string name) => interpreter.GetVariable(name);
        public HLSLValue CallFunction(string name, params HLSLValue[] args) => interpreter.CallFunction(name, args);

        public TestRun[] DiscoverTests(string testFilter = null)
        {
            FunctionDefinitionNode[] functions = interpreter.GetFunctions();
            var testsToRun = new List<TestRun>();

            foreach (var func in functions.Where(x => string.IsNullOrEmpty(testFilter) || Regex.IsMatch(x.Name.GetName(), testFilter)))
            {
                bool hasTestAttribute = false;
                var testCases = new List<(List<HLSLValue> inputs, string formattedName)>();
                TestRun testRun = default;
                testRun.FunctionName = func.Name.GetName();
                testRun.TestName = func.Name.GetName();

                // Detect [MockResource] params first so [TestCase] knows how many args to expect.
                bool hasMocks = TryBuildMockResourceFactories(func, out var mockFactories);
                int nonMockCount = mockFactories.Count(f => f == null);

                foreach (var attribute in func.Attributes)
                {
                    string lexeme = attribute.Name.Identifier.ToLower();
                    switch (lexeme)
                    {
                        case "test":
                            hasTestAttribute = true;
                            break;
                        case "warpsize":
                            if (attribute.Arguments.Count > 1)
                            {
                                testRun.UsesCustomWarpSize = true;
                                testRun.WarpSizeX = Convert.ToInt32((interpreter.EvaluateExpression(attribute.Arguments[0]) as ScalarValue).GetThreadValue(0));
                                testRun.WarpSizeY = Convert.ToInt32((interpreter.EvaluateExpression(attribute.Arguments[1]) as ScalarValue).GetThreadValue(0));
                            }
                            else if (attribute.Arguments.Count > 0)
                            {
                                testRun.UsesCustomWarpSize = true;
                                testRun.WarpSizeX = Convert.ToInt32((interpreter.EvaluateExpression(attribute.Arguments[0]) as ScalarValue).GetThreadValue(0));
                                testRun.WarpSizeY = 1;
                            }
                            break;
                        case "testcase":
                            if (attribute.Arguments.Count == nonMockCount)
                            {
                                var inputs = attribute.Arguments.Select(a => interpreter.EvaluateExpression(a)).ToList();
                                testCases.Add((inputs, $"{testRun.FunctionName}({string.Join(", ", inputs)})"));
                            }
                            break;
                        default: break;
                    }
                }

                if (hasTestAttribute)
                {
                    if (testCases.Count == 0)
                    {
                        if (hasMocks)
                            testRun.GetInputs = () => mockFactories.Select(f => f?.Invoke()).ToList();
                        testsToRun.Add(testRun);
                    }
                    else
                    {
                        foreach (var (caseInputs, formattedName) in testCases)
                        {
                            var caseRun = testRun;
                            caseRun.TestName = formattedName;
                            if (hasMocks)
                            {
                                var capturedCaseInputs = caseInputs;
                                caseRun.GetInputs = () =>
                                {
                                    var merged = new List<HLSLValue>(mockFactories.Length);
                                    int caseIdx = 0;
                                    foreach (var factory in mockFactories)
                                    {
                                        merged.Add(factory != null ? factory() : capturedCaseInputs[caseIdx++]);
                                    }
                                    return merged;
                                };
                            }
                            else
                            {
                                caseRun.GetInputs = () => caseInputs;
                            }
                            testsToRun.Add(caseRun);
                        }
                    }
                }
            }

            return testsToRun.ToArray();
        }

        public TestResult[] RunTests(IEnumerable<TestRun> tests, Action<TestRun> runBeforeTest = null, Action<TestRun, TestResult> runAfterTest = null)
        {
            var testsToRun = tests.ToList();
            TestResult[] results = new TestResult[testsToRun.Count];
            var oldConsoleOut = Console.Out;

            for (int i = 0; i < testsToRun.Count; i++)
            {
                // Setup
                if (testsToRun[i].UsesCustomWarpSize)
                    interpreter.SetWarpSize(testsToRun[i].WarpSizeX, testsToRun[i].WarpSizeY);
                var sw = new StringWriter();
                Console.SetOut(sw);

                // Get inputs
                List<HLSLValue> inputs = new List<HLSLValue>();
                try
                {
                    if (testsToRun[i].GetInputs != null)
                    {
                        inputs = testsToRun[i].GetInputs();
                    }
                }
                catch (Exception ex)
                {
                    results[i] = new TestResult { TestName = testsToRun[i].TestName, Status = TestStatus.Fail, Log = sw.ToString(), Message = $"Error during test input generation: {ex.Message}" };
                }

                // Check if test ignored
                if (IsIgnored(testsToRun[i].FunctionName, inputs, out string reason))
                {
                    results[i] = new TestResult { TestName = testsToRun[i].TestName, Status = TestStatus.Ignored, Log = sw.ToString(), Message = reason };
                    continue;
                }

                // Run pre-amble
                try
                {
                    runBeforeTest?.Invoke(testsToRun[i]);
                }
                catch (Exception ex)
                {
                    results[i] = new TestResult { TestName = testsToRun[i].TestName, Status = TestStatus.Fail, Log = sw.ToString(), Message = $"Error during test setup: {ex.Message}" };
                }

                // Run test
                try
                {
                    interpreter.CallFunction(testsToRun[i].FunctionName, inputs.ToArray());
                    results[i] = new TestResult { TestName = testsToRun[i].TestName, Status = TestStatus.Pass, Log = sw.ToString() };
                }
                catch (TestPassException ex)
                {
                    results[i] = new TestResult { TestName = testsToRun[i].TestName, Status = TestStatus.Pass, Log = sw.ToString(), Message = ex.Message };
                }
                catch (TestFailException ex)
                {
                    results[i] = new TestResult { TestName = testsToRun[i].TestName, Status = TestStatus.Fail, Log = sw.ToString(), Message = ex.Message };
                }
                catch (Exception ex)
                {
                    results[i] = new TestResult { TestName = testsToRun[i].TestName, Status = TestStatus.Fail, Log = sw.ToString(), Message = $"Error during test run: {ex.Message}" };
                }

                // Run post-amble
                try
                {
                    runAfterTest?.Invoke(testsToRun[i], results[i]);
                }
                catch (Exception ex)
                {
                    results[i] = new TestResult { TestName = testsToRun[i].TestName, Status = TestStatus.Fail, Log = sw.ToString(), Message = $"Error during test cleanup: {ex.Message}" };
                }

                // Cleanup
                Console.SetOut(oldConsoleOut);
                if (testsToRun[i].UsesCustomWarpSize)
                    interpreter.SetWarpSize(2, 2);
            }
            return results;
        }

        public TestResult[] RunTests(string testFilter = null, Action<TestRun> runBeforeTest = null, Action<TestRun, TestResult> runAfterTest = null)
        {
            return RunTests(DiscoverTests(testFilter), runBeforeTest, runAfterTest);
        }
    }
}
