using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityShaderParser.Common;
using UnityShaderParser.HLSL;

namespace HLSL
{
    public class HLSLRunner
    {
        [Serializable]
        public class TestFailException : Exception
        {
            public TestFailException() { }
            public TestFailException(string message) : base(message) { }
            public TestFailException(string message, Exception innerException) : base(message, innerException) { }
        }

        [Serializable]
        public class TestPassException : Exception
        {
            public TestPassException() { }
            public TestPassException(string message) : base(message) { }
            public TestPassException(string message, Exception innerException) : base(message, innerException) { }
        }

        [Serializable]
        public class TestIgnoreException : Exception
        {
            public TestIgnoreException() { }
            public TestIgnoreException(string message) : base(message) { }
            public TestIgnoreException(string message, Exception innerException) : base(message, innerException) { }
        }

        public enum TestStatus { Pass, Fail, Ignored }

        public struct TestRun
        {
            public string TestName;
            public string FunctionName;
            public string Description;
            public string Category;

            public bool UsesCustomWarpSize;
            public int WarpSizeX;
            public int WarpSizeY;

            public bool UsesCustomThreadGroup;
            public int ThreadGroupX;
            public int ThreadGroupY;
            public int ThreadGroupZ;

            public Func<HLSLRunner, List<HLSLValue>> InputGenerator;
        }

        public struct TestResult
        {
            public string TestName;
            public TestStatus Status;
            public string Message;
            public string Log;
        }

        protected HLSLInterpreter interpreter;
        protected TestRun currentTest;

        private List<List<HLSLValue>> collectedCases;
        private List<HLSLValue> collectedValues;

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
                        if (state.IsThreadActive(i) && !sv.AsBool(i))
                            throw new TestFailException(failMsg);
                }
                else if (val is VectorValue vv)
                {
                    for (int i = 0; i < vv.ThreadCount; i++)
                        if (state.IsThreadActive(i))
                            for (int c = 0; c < vv.Size; c++)
                                if (!vv[c].AsBool(i))
                                    throw new TestFailException(failMsg);
                }
                else if (val is MatrixValue mv)
                {
                    for (int i = 0; i < mv.ThreadCount; i++)
                        if (state.IsThreadActive(i))
                            for (int c = 0; c < mv.Rows * mv.Columns; c++)
                                if (!mv[c].AsBool(i))
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
                        message = (interpreter.EvaluateExpression(args[1]) as StringValue)?.Value;

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
                    throw new TestPassException();
                }
                return ScalarValue.Null;
            });

            interpreter.AddCallback("PASS_TEST_MSG", (state, args) =>
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
                    throw new TestFailException();
                }
                return ScalarValue.Null;
            });

            interpreter.AddCallback("FAIL_TEST_MSG", (state, args) =>
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

            interpreter.AddCallback("IGNORE_TEST", (state, args) =>
            {
                if (state.IsAnyThreadActive())
                {
                    throw new TestIgnoreException();
                }
                return ScalarValue.Null;
            });

            interpreter.AddCallback("IGNORE_TEST_MSG", (state, args) =>
            {
                if (state.IsAnyThreadActive())
                {
                    if (args.Length > 0)
                        throw new TestIgnoreException(interpreter.EvaluateExpression(args[0]).ToString());
                    else
                        throw new TestIgnoreException();
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
                    ?? throw new Exception(PrintingUtil.FormatError(args[0].Span, "First argument to MOCK_RESOURCE must be a resource variable name."));
                string mockStructName = (args[1] as IdentifierExpressionNode)?.GetName()
                    ?? throw new Exception(PrintingUtil.FormatError(args[1].Span, "Second argument to MOCK_RESOURCE must be a mock struct type name."));

                var existing = interpreter.GetVariable(resourceName) as ResourceValue
                    ?? throw new Exception(PrintingUtil.FormatError(args[0].Span, $"MOCK_RESOURCE: '{resourceName}' is not a resource variable."));

                var mock = interpreter.CreateMockResource(mockStructName, existing.Type, existing.TemplateArguments);
                interpreter.SetVariable(resourceName, mock);
                return ScalarValue.Null;
            });

            interpreter.AddCallback("TEST_NAME", (state, args) =>
            {
                return new StringValue(currentTest.TestName);
            });

            interpreter.AddCallback("TEST_CASE", (state, args) =>
            {
                if (collectedCases != null)
                {
                    var inputs = args.Select(a => interpreter.EvaluateExpression(a)).ToList();
                    collectedCases.Add(inputs);
                }
                return ScalarValue.Null;
            });

            interpreter.AddCallback("TEST_VALUE", (state, args) =>
            {
                if (collectedValues != null && args.Length > 0)
                {
                    collectedValues.Add(interpreter.EvaluateExpression(args[0]));
                }
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

        // Describes how to populate input for a specific test function parameter.
        private struct ParamFill
        {
            public Func<HLSLRunner, HLSLValue> Factory; // Singular, i.e. [MockResource], SV_*
            public List<HLSLValue> ValueSet;            // Combinatiorial, i.e. [Values], [ValueSource]
        }

        private ParamFill[] BuildParamFills(FunctionDefinitionNode func)
        {
            var fills = new ParamFill[func.Parameters.Count];

            for (int i = 0; i < func.Parameters.Count; i++)
            {
                var param = func.Parameters[i];

                foreach (var attr in param.Attributes)
                {
                    string attrName = attr.Name.Identifier.ToLowerInvariant();

                    if (attrName == "mockresource" && attr.Arguments.Count > 0)
                    {
                        string mockStructName = (attr.Arguments[0] as IdentifierExpressionNode)?.GetName()
                            ?? (interpreter.EvaluateExpression(attr.Arguments[0]) as StringValue)?.Value;
                        if (mockStructName == null)
                            continue;
                        if (param.ParamType is not PredefinedObjectTypeNode resourceTypeNode)
                            continue;

                        var capturedStructName = mockStructName;
                        var capturedKind = resourceTypeNode.Kind;
                        var capturedTemplateArgs = resourceTypeNode.TemplateArguments.ToArray();
                        fills[i].Factory = runner => runner.interpreter.CreateMockResource(capturedStructName, capturedKind, capturedTemplateArgs);
                    }
                    else if (attrName == "values" && attr.Arguments.Count > 0)
                    {
                        fills[i].ValueSet = attr.Arguments.Select(a => interpreter.EvaluateExpression(a)).ToList();
                    }
                    else if (attrName == "valuesource" && attr.Arguments.Count == 1)
                    {
                        string genName = (attr.Arguments[0] as IdentifierExpressionNode)?.GetName()
                            ?? (interpreter.EvaluateExpression(attr.Arguments[0]) as StringValue)?.Value;
                        if (genName != null)
                            fills[i].ValueSet = RunValueGenerator(genName);
                    }
                }

                // SV semantics auto fill
                foreach (var qual in param.Declarator.Qualifiers)
                {
                    if (qual is not SemanticNode semanticNode)
                        continue;

                    switch (semanticNode.Name.Identifier?.ToLowerInvariant())
                    {
                        case "sv_groupindex":
                            fills[i].Factory = static runner =>
                            {
                                var es = runner.GetExecutionState();
                                int threadCount = es.GetThreadCount();
                                int gx = es.GetSizeX();
                                int gy = es.GetSizeY();
                                var lanes = new RawValue[threadCount];
                                for (int threadIndex = 0; threadIndex < threadCount; threadIndex++)
                                {
                                    var (tx, ty, tz) = es.GetThreadPosition(threadIndex);
                                    lanes[threadIndex] = (uint)(tz * gx * gy + ty * gx + tx);
                                }
                                return new ScalarValue(ScalarType.Uint, HLSLValueUtils.MakeScalarVGPR(lanes));
                            };
                            break;
                        case "sv_groupthreadid":
                        case "sv_dispatchthreadid":
                            fills[i].Factory = static runner =>
                            {
                                var es = runner.GetExecutionState();
                                int threadCount = es.GetThreadCount();
                                var lanes = new RawValue[threadCount][];
                                for (int threadIndex = 0; threadIndex < threadCount; threadIndex++)
                                {
                                    var (tx, ty, tz) = es.GetThreadPosition(threadIndex);
                                    lanes[threadIndex] = new RawValue[] { (uint)tx, (uint)ty, (uint)tz };
                                }
                                return new VectorValue(ScalarType.Uint, HLSLValueUtils.MakeVectorVGPR(lanes));
                            };
                            break;
                        case "sv_groupid":
                            fills[i].Factory = static _ => new VectorValue(ScalarType.Uint,
                                HLSLValueUtils.MakeVectorSGPR(new RawValue[] { (uint)0, (uint)0, (uint)0 }));
                            break;
                        default:
                            break;
                    }
                }
            }

            return fills;
        }

        private bool IsIgnored(string functionName, List<HLSLValue> inputs, out string reason)
        {
            reason = null;
            var func = interpreter.GetFunction(functionName, inputs.ToArray());
            if (func == null) return false;
            foreach (var attr in func.Attributes.Where(x => x.Name.Identifier.ToLower() == "ignore"))
            {
                if (attr.Arguments.Count > 0)
                    reason = interpreter.EvaluateExpression(attr.Arguments[0]).ToString();
                return true;
            }
            return false;
        }

        private List<List<HLSLValue>> RunTestCaseGenerator(string funcName)
        {
            collectedCases = new List<List<HLSLValue>>();
            try
            {
                interpreter.CallFunction(funcName);
            }
            catch (Exception ex)
            {
                collectedCases = null;
                throw new TestFailException($"Error in TestCaseSource generator '{funcName}': {ex.Message}", ex);
            }
            var result = collectedCases;
            collectedCases = null;
            return result;
        }

        private List<HLSLValue> RunValueGenerator(string funcName)
        {
            collectedValues = new List<HLSLValue>();
            try
            {
                interpreter.CallFunction(funcName);
            }
            catch (Exception ex)
            {
                collectedValues = null;
                throw new TestFailException($"Error in ValueSource generator '{funcName}': {ex.Message}", ex);
            }
            var result = collectedValues;
            collectedValues = null;
            return result;
        }

        private static IEnumerable<IEnumerable<T>> CartesianProduct<T>(IEnumerable<IEnumerable<T>> sequences)
        {
            IEnumerable<IEnumerable<T>> seed = new[] { Enumerable.Empty<T>() };
            return sequences.Aggregate(seed, (acc, seq) =>
                acc.SelectMany(a => seq.Select(s => a.Concat(new[] { s }))));
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

        public void SetVariable(string name, HLSLValue value) => interpreter.SetVariable(name, value);
        public HLSLValue GetVariable(string name) => interpreter.GetVariable(name);
        public HLSLValue CallFunction(string name, params HLSLValue[] args) => interpreter.CallFunction(name, args);

        public void SetWarpSize(int warpSizeX, int warpSizeY) => interpreter.SetWarpSize(warpSizeX, warpSizeY);
        public void SetWarpAndGroupSize(int warpSizeX, int warpSizeY, int groupSizeX, int groupSizeY, int groupSizeZ) => interpreter.SetWarpAndGroupSize(warpSizeX, warpSizeY, groupSizeX, groupSizeY, groupSizeZ);
        public HLSLExecutionState GetExecutionState() => interpreter.GetExecutionState();

        // Reflection API
        public FunctionDefinitionNode GetFunction(string name, HLSLValue[] args) => interpreter.GetFunction(name, args);
        public FunctionDefinitionNode GetFunction(string name) => interpreter.GetFunction(name);
        public StructTypeNode GetStructType(string name) => interpreter.GetStructType(name);
        public TypeNode ResolveType(TypeNode type) => interpreter.ResolveType(type);

        // Debug API
        public Action<HLSLSyntaxNode> DebugHookBeforeStatement
        {
            get => interpreter.DebugHookBeforeStatement;
            set => interpreter.DebugHookBeforeStatement = value;
        }
        public Action<HLSLSyntaxNode> DebugHookAfterStatement
        {
            get => interpreter.DebugHookAfterStatement;
            set => interpreter.DebugHookAfterStatement = value;
        }
        public Dictionary<string, HLSLValue> GetVisibleVariables() => interpreter.GetVisibleVariables();
        public Dictionary<string, HLSLValue>[] GetVariablesPerFrame() => interpreter.GetVariablesPerFrame();
        public Dictionary<string, HLSLValue> GetGlobalVariables() => interpreter.GetGlobalVariables();
        public HLSLExecutionState.ThreadState[] GetThreadStates() => interpreter.GetThreadStates();
        public HLSLExecutionState.ThreadState[][] GetThreadStatesPerFrame() => interpreter.GetThreadStatesPerFrame();
        public string[] GetCallStack() => interpreter.GetCallStack();
        public HLSLValue EvaluateExpression(ExpressionNode node) => interpreter.EvaluateExpression(node);
        public HLSLValue EvaluateExpression(string source) => interpreter.EvaluateExpression(source);

        public TestRun[] DiscoverTests(string testFilter = null)
        {
            var functions = interpreter.GetFunctions();
            var testsToRun = new List<TestRun>();

            foreach (var (qualifiedName, func) in functions.Where(x => string.IsNullOrEmpty(testFilter) || Regex.IsMatch(x.Item1, testFilter)))
            {
                bool hasTestAttribute = false;
                var testCases = new List<(List<HLSLValue> inputs, string formattedName)>();
                TestRun testRun = default;
                testRun.FunctionName = qualifiedName;
                testRun.TestName = func.Name.GetName();

                // Detect per-parameter input sources first so [TestCase] knows how many args to expect.
                var paramFills = BuildParamFills(func);
                int testCaseArity = paramFills.Count(f => f.Factory == null);
                int externalInputCount = paramFills.Count(f => f.Factory == null && f.ValueSet == null);
                bool hasAutoFill = testCaseArity < paramFills.Length;
                bool hasValueSet = testCaseArity > externalInputCount;

                // Gather test attributes
                foreach (var attribute in func.Attributes)
                {
                    string lexeme = attribute.Name.Identifier.ToLowerInvariant();
                    switch (lexeme)
                    {
                        case "test":
                            hasTestAttribute = true;
                            break;
                        case "warpsize":
                            if (attribute.Arguments.Count > 1)
                            {
                                testRun.UsesCustomWarpSize = true;
                                testRun.WarpSizeX = (interpreter.EvaluateExpression(attribute.Arguments[0]) as ScalarValue).AsInt();
                                testRun.WarpSizeY = (interpreter.EvaluateExpression(attribute.Arguments[1]) as ScalarValue).AsInt();
                            }
                            else if (attribute.Arguments.Count > 0)
                            {
                                testRun.UsesCustomWarpSize = true;
                                testRun.WarpSizeX = (interpreter.EvaluateExpression(attribute.Arguments[0]) as ScalarValue).AsInt();
                                testRun.WarpSizeY = 1;
                            }
                            break;
                        case "numthreads":
                            if (attribute.Arguments.Count >= 1)
                            {
                                testRun.UsesCustomThreadGroup = true;
                                testRun.ThreadGroupX = (interpreter.EvaluateExpression(attribute.Arguments[0]) as ScalarValue).AsInt();
                                testRun.ThreadGroupY = attribute.Arguments.Count > 1
                                    ? (interpreter.EvaluateExpression(attribute.Arguments[1]) as ScalarValue).AsInt() : 1;
                                testRun.ThreadGroupZ = attribute.Arguments.Count > 2
                                    ? (interpreter.EvaluateExpression(attribute.Arguments[2]) as ScalarValue).AsInt() : 1;
                            }
                            break;
                        case "testcase":
                            if (attribute.Arguments.Count == testCaseArity)
                            {
                                var inputs = attribute.Arguments.Select(a => interpreter.EvaluateExpression(a)).ToList();
                                testCases.Add((inputs, $"{testRun.FunctionName}({string.Join(", ", inputs)})"));
                            }
                            break;
                        case "testcasesource":
                            if (attribute.Arguments.Count == 1)
                            {
                                string generatorName = (attribute.Arguments[0] as IdentifierExpressionNode)?.GetName()
                                    ?? (interpreter.EvaluateExpression(attribute.Arguments[0]) as StringValue)?.Value;
                                if (generatorName != null)
                                {
                                    var cases = RunTestCaseGenerator(generatorName);
                                    foreach (var caseInputs in cases.Where(c => c.Count == testCaseArity))
                                    {
                                        testCases.Add((caseInputs, $"{testRun.FunctionName}({string.Join(", ", caseInputs)})"));
                                    }
                                }
                            }
                            break;
                        case "description":
                            if (attribute.Arguments.Count > 0)
                                testRun.Description = (interpreter.EvaluateExpression(attribute.Arguments[0]) as StringValue)?.Value ?? "";
                            break;
                        case "category":
                            if (attribute.Arguments.Count > 0)
                                testRun.Category = (interpreter.EvaluateExpression(attribute.Arguments[0]) as StringValue)?.Value ?? "";
                            break;
                        default: break;
                    }
                }

                // Process per-parameter [Values] and [ValueSource] attributes combinatorially.
                if (hasValueSet && !hasAutoFill && externalInputCount == 0)
                {
                    foreach (var combo in CartesianProduct(paramFills.Select(f => f.ValueSet)))
                    {
                        var comboList = combo.ToList();
                        testCases.Add((comboList, $"{testRun.FunctionName}({string.Join(", ", comboList)})"));
                    }
                }

                // If we have the test attribute, register the tests
                if (hasTestAttribute)
                {
                    // Simple test
                    if (testCases.Count == 0)
                    {
                        if (hasAutoFill)
                            testRun.InputGenerator = runner => paramFills.Select(f => f.Factory?.Invoke(runner)).ToList();
                        testsToRun.Add(testRun);
                    }
                    // Test with cases
                    else
                    {
                        foreach (var (caseInputs, formattedName) in testCases)
                        {
                            var caseRun = testRun;
                            caseRun.TestName = formattedName;
                            // If we have autofills, include them
                            if (hasAutoFill)
                            {
                                caseRun.InputGenerator = runner =>
                                {
                                    var merged = new List<HLSLValue>(paramFills.Length);
                                    int caseIdx = 0;
                                    foreach (var fill in paramFills)
                                    {
                                        merged.Add(fill.Factory != null ? fill.Factory(runner) : caseInputs[caseIdx++]);
                                    }
                                    return merged;
                                };
                            }
                            // Otherwise use the regular generator
                            else
                            {
                                caseRun.InputGenerator = _ => caseInputs;
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
                currentTest = testsToRun[i];

                // Setup
                var state = interpreter.GetExecutionState();
                int prevWarpSizeX = state.GetWarpSizeX(), prevWarpSizeY = state.GetWarpSizeY();
                int prevGroupSizeX = state.GetSizeX(), prevGroupSizeY = state.GetSizeY(), prevGroupSizeZ = state.GetSizeZ();
                bool customThreadLayout = currentTest.UsesCustomWarpSize || currentTest.UsesCustomThreadGroup;
                if (customThreadLayout)
                {
                    int warpSizeX = currentTest.UsesCustomWarpSize ? currentTest.WarpSizeX : prevWarpSizeX;
                    int warpSizeY = currentTest.UsesCustomWarpSize ? currentTest.WarpSizeY : prevWarpSizeY;
                    int groupSizeX = currentTest.UsesCustomThreadGroup ? currentTest.ThreadGroupX : warpSizeX;
                    int groupSizeY = currentTest.UsesCustomThreadGroup ? currentTest.ThreadGroupY : warpSizeY;
                    int groupSizeZ = currentTest.UsesCustomThreadGroup ? currentTest.ThreadGroupZ : prevGroupSizeZ;
                    interpreter.SetWarpAndGroupSize(warpSizeX, warpSizeY, groupSizeX, groupSizeY, groupSizeZ);
                }
                var sw = new StringWriter();
                Console.SetOut(sw);

                // Get inputs
                List<HLSLValue> inputs = new List<HLSLValue>();
                try
                {
                    if (currentTest.InputGenerator != null)
                    {
                        inputs = currentTest.InputGenerator(this);
                    }
                }
                catch (Exception ex)
                {
                    results[i] = new TestResult { TestName = currentTest.TestName, Status = TestStatus.Fail, Log = sw.ToString(), Message = $"Error during test input generation: {ex.Message}" };
                }

                // Check if test ignored
                if (IsIgnored(currentTest.FunctionName, inputs, out string reason))
                {
                    results[i] = new TestResult { TestName = currentTest.TestName, Status = TestStatus.Ignored, Log = sw.ToString(), Message = reason };
                    continue;
                }

                // Run pre-amble
                try
                {
                    runBeforeTest?.Invoke(currentTest);
                }
                catch (Exception ex)
                {
                    results[i] = new TestResult { TestName = currentTest.TestName, Status = TestStatus.Fail, Log = sw.ToString(), Message = $"Error during test setup: {ex.Message}" };
                }

                // Run test
                try
                {
                    interpreter.CallFunction(currentTest.FunctionName, inputs.ToArray());
                    results[i] = new TestResult { TestName = currentTest.TestName, Status = TestStatus.Pass, Log = sw.ToString() };
                }
                catch (TestPassException ex)
                {
                    results[i] = new TestResult { TestName = currentTest.TestName, Status = TestStatus.Pass, Log = sw.ToString(), Message = ex.Message };
                }
                catch (TestFailException ex)
                {
                    results[i] = new TestResult { TestName = currentTest.TestName, Status = TestStatus.Fail, Log = sw.ToString(), Message = ex.Message };
                }
                catch (TestIgnoreException ex)
                {
                    results[i] = new TestResult { TestName = currentTest.TestName, Status = TestStatus.Ignored, Log = sw.ToString(), Message = ex.Message };
                }
                catch (Exception ex)
                {
                    results[i] = new TestResult { TestName = currentTest.TestName, Status = TestStatus.Fail, Log = sw.ToString(), Message = $"Error during test run: {ex.Message}" };
                }

                // Run post-amble
                try
                {
                    runAfterTest?.Invoke(currentTest, results[i]);
                }
                catch (Exception ex)
                {
                    results[i] = new TestResult { TestName = currentTest.TestName, Status = TestStatus.Fail, Log = sw.ToString(), Message = $"Error during test cleanup: {ex.Message}" };
                }

                // Cleanup
                Console.SetOut(oldConsoleOut);
                if (customThreadLayout)
                {
                    interpreter.SetWarpAndGroupSize(prevWarpSizeX, prevWarpSizeY,
                        prevGroupSizeX, prevGroupSizeY, prevGroupSizeZ);
                }
            }
            return results;
        }

        public TestResult[] RunTests(string testFilter = null, Action<TestRun> runBeforeTest = null, Action<TestRun, TestResult> runAfterTest = null)
        {
            return RunTests(DiscoverTests(testFilter), runBeforeTest, runAfterTest);
        }
    }
}
