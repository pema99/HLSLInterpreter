using System;
using System.Collections.Generic;
using System.Linq;
using UnityShaderParser.Common;
using UnityShaderParser.HLSL;

namespace HLSLInterpreter
{
    public class HLSLInterpreter : HLSLSyntaxVisitor
    {
        protected HLSLInterpreterContext context;
        protected HLSLExecutionState executionState;
        protected HLSLExpressionEvaluator expressionEvaluator;

        public HLSLInterpreter(int threadsX = 2, int threadsY = 2)
        {
            context = new HLSLInterpreterContext();
            executionState = new HLSLExecutionState(threadsX, threadsY);
            expressionEvaluator = new HLSLExpressionEvaluator(this, context, executionState);
        }

        // Public interface
        public void SetWarpSize(int threadsX, int threadsY)
        {
            var oldCallbacks = expressionEvaluator.GetCallbacks();
            executionState = new HLSLExecutionState(threadsX, threadsY);
            expressionEvaluator = new HLSLExpressionEvaluator(this, context, executionState);
            foreach (var kvp in oldCallbacks)
                expressionEvaluator.AddCallback(kvp.Key, kvp.Value);
        }

        public void SetVariable(string name, HLSLValue value) => context.SetVariable(name, value);
        public HLSLValue GetVariable(string name) => context.GetVariable(name);

        public void Reset()
        {
            var oldCallbacks = expressionEvaluator.GetCallbacks();
            context = new HLSLInterpreterContext();
            executionState = new HLSLExecutionState(executionState.GetThreadsX(), executionState.GetThreadsY());
            expressionEvaluator = new HLSLExpressionEvaluator(this, context, executionState);
            foreach (var kvp in oldCallbacks)
                expressionEvaluator.AddCallback(kvp.Key, kvp.Value);
        }

        public void AddCallback(string name, Func<HLSLExecutionState, ExpressionNode[], HLSLValue> callback) => expressionEvaluator.AddCallback(name, callback);
        public void RemoveCallback(string name) => expressionEvaluator.RemoveCallback(name);

        public HLSLValue CallFunction(string name, params HLSLValue[] args) => expressionEvaluator.CallFunction(name, args);
        public FunctionDefinitionNode GetFunction(string name, HLSLValue[] args) => context.GetFunction(expressionEvaluator, name, args);

        public (string QualifiedName, FunctionDefinitionNode Func)[] GetFunctions() => context.GetFunctions();

        public HLSLValue EvaluateExpression(ExpressionNode node) => expressionEvaluator.Visit(node);

        public ResourceValue CreateMockResource(string structName, PredefinedObjectType resourceType, TypeNode[] templateArgs)
        {
            var structDef = context.GetStruct(structName) ?? throw Error($"Unknown type '{structName}'.");
            var mockStruct = CreateStructValue(structDef);

            bool HasMethod(string name)
            {
                return structDef.Methods
                    .OfType<FunctionDefinitionNode>()
                    .Any(m => m.Name.GetName() == name);
            }

            Func<int> MakeSizeDelegate(string methodName, int defaultValue)
            {
                if (!HasMethod(methodName)) return () => defaultValue;
                return () =>
                {
                    var res = expressionEvaluator.CallMethod(mockStruct, methodName, Array.Empty<HLSLValue>());
                    return res is ScalarValue sv ? Convert.ToInt32(sv.GetThreadValue(0)) : defaultValue;
                };
            }

            if (HasMethod("Initialize"))
                expressionEvaluator.CallMethod(mockStruct, "Initialize", Array.Empty<HLSLValue>());

            ResourceGetter getter = (x, y, z, w, mip) => (NumericValue)0;
            ResourceSetter setter = (x, y, z, w, mip, val) => { };
            if (HasMethod("Read"))
            {
                getter = (x, y, z, w, mip) =>
                {
                    var args = new HLSLValue[] { (NumericValue)x, (NumericValue)y, (NumericValue)z, (NumericValue)w, (NumericValue)mip };
                    var res = expressionEvaluator.CallMethod(mockStruct, "Read", args);
                    return res;
                };
            }
            if (HasMethod("Write"))
            {
                setter = (x, y, z, w, mip, val) =>
                {
                    var args = new HLSLValue[] { (NumericValue)x, (NumericValue)y, (NumericValue)z, (NumericValue)w, (NumericValue)mip, val };
                    expressionEvaluator.CallMethod(mockStruct, "Write", args);
                };
            }

            int stride = templateArgs.Length > 0 ? HLSLTypeUtils.GetTypeSizeDwords(context, templateArgs[0]) * 4 : 0;
            return new ResourceValue(
                resourceType, templateArgs, stride,
                MakeSizeDelegate("SizeX", 2),
                MakeSizeDelegate("SizeY", 2),
                MakeSizeDelegate("SizeZ", 2),
                MakeSizeDelegate("MipCount", 1),
                getter, setter);
        }

        // Helpers
        private Exception Error(HLSLSyntaxNode node, string message)
        {
            return new Exception($"Error at line {node.Span.Start.Line}, column {node.Span.Start.Column}: {message}");
        }

        private Exception Error(string message)
        {
            return new Exception($"Error: {message}");
        }

        private NumericValue EvaluateNumeric(ExpressionNode node, ScalarType type = ScalarType.Void)
        {
            var value = EvaluateExpression(node);
            if (value is NumericValue num)
            {
                if (type != ScalarType.Void && num.Type != type)
                    throw Error(node, $"Expected an expression of type '{PrintingUtil.GetEnumName(type)}', but got one of type '{PrintingUtil.GetEnumName(num.Type)}'.");
                return num;
            }
            else if (value is ReferenceValue refVal && refVal.Get() is NumericValue refNum)
            {
                if (type != ScalarType.Void && refNum.Type != type)
                    throw Error(node, $"Expected an expression of type '{PrintingUtil.GetEnumName(type)}', but got one of type '{PrintingUtil.GetEnumName(refNum.Type)}'.");
                return refNum;
            }
            else
            {
                throw Error(node, $"Expected a numeric expression, but got a {value.GetType().Name}.");
            }
        }

        private ScalarValue EvaluateScalar(ExpressionNode node, ScalarType type = ScalarType.Void)
        {
            var value = EvaluateExpression(node);
            if (value is ScalarValue num)
            {
                if (type != ScalarType.Void && num.Type != type)
                    throw Error(node, $"Expected an expression of type '{PrintingUtil.GetEnumName(type)}', but got one of type '{PrintingUtil.GetEnumName(num.Type)}'.");
                return num;
            }
            else if (value is ReferenceValue refVal && refVal.Get() is ScalarValue refNum)
            {
                if (type != ScalarType.Void && refNum.Type != type)
                    throw Error(node, $"Expected an expression of type '{PrintingUtil.GetEnumName(type)}', but got one of type '{PrintingUtil.GetEnumName(refNum.Type)}'.");
                return refNum;
            }
            else
            {
                throw Error(node, $"Expected a scalar expression, but got a {value.GetType().Name}.");
            }
        }

        private HLSLValue GetVariableDeclarationInitialValue(TypeNode type, VariableDeclaratorNode decl)
        {
            type = context.ResolveType(type);
            bool isArray = decl.ArrayRanks.Count > 0;
            bool hasImplicitSize = isArray && decl.ArrayRanks[0].Dimension == null;
            int arrayLength = (isArray && !hasImplicitSize) ? Convert.ToInt32(EvaluateNumeric(decl.ArrayRanks[0].Dimension).GetThreadValue(0)) : 0;
            
            if (decl.Initializer is ValueInitializerNode initializer)
            {
                HLSLValue initializerValue;
                if (type is NumericTypeNode numericType && !isArray)
                {
                    // Array-literal initializer for a vector/matrix: float3 v = {1,2,3}
                    var rawValue = EvaluateExpression(initializer.Expression);
                    if (rawValue is ArrayValue arrayInit)
                        return HLSLValueUtils.PackScalars(context, HLSLValueUtils.FlattenToScalars(arrayInit), numericType);

                    // Regular numeric initializer
                    if (rawValue is not NumericValue numericInitializerValue)
                        throw Error(initializer.Expression, $"Expected a numeric expression, but got a {rawValue.GetType().Name}.");
                    switch (type)
                    {
                        case ScalarTypeNode scalarType:
                            initializerValue = numericInitializerValue.Cast(scalarType.Kind);
                            break;
                        case VectorTypeNode vectorType:
                            initializerValue = numericInitializerValue.Cast(vectorType.Kind)
                                .BroadcastToVector(vectorType.Dimension);
                            break;
                        case MatrixTypeNode matrixType:
                            initializerValue = numericInitializerValue.Cast(matrixType.Kind)
                                .BroadcastToMatrix(matrixType.FirstDimension, matrixType.SecondDimension);
                            break;
                        case GenericVectorTypeNode genVectorType:
                            int dims = Convert.ToInt32(EvaluateScalar(genVectorType.Dimension).Cast(ScalarType.Int).GetThreadValue(0));
                            initializerValue = numericInitializerValue.Cast(genVectorType.Kind).BroadcastToVector(dims);
                            break;
                        case GenericMatrixTypeNode genMatrixType:
                            int rows = Convert.ToInt32(EvaluateScalar(genMatrixType.FirstDimension).Cast(ScalarType.Int).GetThreadValue(0));
                            int cols = Convert.ToInt32(EvaluateScalar(genMatrixType.SecondDimension).Cast(ScalarType.Int).GetThreadValue(0));
                            initializerValue = numericInitializerValue.Cast(genMatrixType.Kind).BroadcastToMatrix(rows, cols);
                            break;
                        default:
                            throw Error(decl, "Invalid variable declaration");
                    }
                }
                else
                {
                    initializerValue = EvaluateExpression(initializer.Expression);

                    // HLSL ignores braces and flattens all scalar components in an initializer, then repacks
                    // into the target element type.
                    if (initializerValue is ArrayValue flatInitVal)
                    {
                        int scalarsPerElement = HLSLTypeUtils.GetTypeSizeDwords(context, type);
                        if (scalarsPerElement > 0)
                        {
                            var flattened = HLSLValueUtils.FlattenToScalars(flatInitVal);
                            if (isArray)
                            {
                                int count = hasImplicitSize ? flattened.Length / scalarsPerElement : arrayLength;
                                var elements = new HLSLValue[count];
                                for (int i = 0; i < count; i++)
                                    elements[i] = HLSLValueUtils.PackScalars(context, flattened[(i * scalarsPerElement)..((i + 1) * scalarsPerElement)], type);
                                return new ArrayValue(elements);
                            }
                            else
                            {
                                return HLSLValueUtils.PackScalars(context, flattened, type);
                            }
                        }
                    }
                }

                return initializerValue;
            }
            else if (decl.Initializer is StateInitializerNode stateInit)
            {
                bool isCmp = type is PredefinedObjectTypeNode pot && pot.Kind == PredefinedObjectType.SamplerComparisonState;
                return HLSLSamplerStateBuilder.Build(isCmp, stateInit.States, expressionEvaluator);
            }
            else if (decl.Initializer is StateArrayInitializerNode stateArrayInit && stateArrayInit.Initializers.Count > 0)
            {
                bool isCmp = type is PredefinedObjectTypeNode pot && pot.Kind == PredefinedObjectType.SamplerComparisonState;
                return HLSLSamplerStateBuilder.Build(isCmp, stateArrayInit.Initializers[0].States, expressionEvaluator);
            }
            else
            {
                HLSLValue defaultValue;
                switch (type)
                {
                    case ScalarTypeNode scalarType:
                        defaultValue = new ScalarValue(scalarType.Kind, new HLSLRegister<object>(HLSLTypeUtils.GetZeroValue(scalarType.Kind)));
                        break;
                    case VectorTypeNode vectorType:
                        defaultValue = new VectorValue(vectorType.Kind,
                            new HLSLRegister<object[]>(Enumerable.Repeat(HLSLTypeUtils.GetZeroValue(vectorType.Kind), vectorType.Dimension).ToArray()));
                        break;
                    case MatrixTypeNode matrixType:
                        defaultValue = new MatrixValue(matrixType.Kind, matrixType.FirstDimension, matrixType.SecondDimension,
                            new HLSLRegister<object[]>(Enumerable.Repeat(HLSLTypeUtils.GetZeroValue(matrixType.Kind), matrixType.FirstDimension * matrixType.SecondDimension).ToArray()));
                        break;
                    case QualifiedNamedTypeNode qualifiedNamedTypeNodeType:
                        string fullName = qualifiedNamedTypeNodeType.GetName();
                        string[] namespaces = fullName.Split("::");
                        for (int i = 0; i < namespaces.Length - 1; i++)
                            context.EnterNamespace(namespaces[i]);

                        var qualNamedStruct = context.GetStruct(qualifiedNamedTypeNodeType.GetName());
                        if (qualNamedStruct == null)
                            throw Error(decl, $"Undefined named type '{qualifiedNamedTypeNodeType.GetName()}'.");
                        defaultValue = CreateStructValue(qualNamedStruct);

                        for (int i = 0; i < namespaces.Length - 1; i++)
                            context.ExitNamespace();
                        break;
                    case NamedTypeNode namedTypeNodeType:
                        var namedStruct = context.GetStruct(namedTypeNodeType.GetName());
                        if (namedStruct == null)
                            throw Error(decl, $"Undefined named type '{namedTypeNodeType.GetName()}'.");
                        defaultValue = CreateStructValue(namedStruct);
                        break;
                    case GenericVectorTypeNode genVectorType:
                        int dims = Convert.ToInt32(EvaluateScalar(genVectorType.Dimension).Cast(ScalarType.Int).GetThreadValue(0));
                        defaultValue = new VectorValue(genVectorType.Kind,
                            new HLSLRegister<object[]>(Enumerable.Repeat(HLSLTypeUtils.GetZeroValue(genVectorType.Kind), dims).ToArray()));
                        break;
                    case GenericMatrixTypeNode genMatrixType:
                        int rows = Convert.ToInt32(EvaluateScalar(genMatrixType.FirstDimension).Cast(ScalarType.Int).GetThreadValue(0));
                        int cols = Convert.ToInt32(EvaluateScalar(genMatrixType.SecondDimension).Cast(ScalarType.Int).GetThreadValue(0));
                        defaultValue = new MatrixValue(genMatrixType.Kind, rows, cols,
                           new HLSLRegister<object[]>(Enumerable.Repeat(HLSLTypeUtils.GetZeroValue(genMatrixType.Kind), rows * cols).ToArray()));
                        break;
                    case PredefinedObjectTypeNode predefinedObjectType:
                        switch (predefinedObjectType.Kind)
                        {
                            case PredefinedObjectType.Sampler:
                            case PredefinedObjectType.SamplerState:
                                defaultValue = new SamplerStateValue(false);
                                break;
                            case PredefinedObjectType.SamplerComparisonState:
                                defaultValue = new SamplerStateValue(true);
                                break;
                            default:
                                if (HLSLSyntaxFacts.IsTexture(predefinedObjectType.Kind) || HLSLSyntaxFacts.IsBuffer(predefinedObjectType.Kind))
                                {
                                    int dim = HLSLSyntaxFacts.GetDimension(predefinedObjectType.Kind);
                                    var templateArgs = predefinedObjectType.TemplateArguments.ToArray();
                                    int stride = templateArgs.Length > 0 ? HLSLTypeUtils.GetTypeSizeDwords(context, templateArgs[0]) * 4 : 0;
                                    defaultValue = new ResourceValue(
                                        predefinedObjectType.Kind,
                                        templateArgs, stride,
                                        dim >= 1 ? 2 : 1,
                                        dim >= 2 ? 2 : 1,
                                        dim >= 3 ? 2 : 1,
                                        1,
                                        (x, y, z, w, mip) => (NumericValue)0,
                                        (x, y, z, w, mip, val) => { });
                                }
                                else
                                {
                                    defaultValue = new PredefinedObjectValue(predefinedObjectType.Kind, predefinedObjectType.TemplateArguments.ToArray());
                                }
                                break;
                        }
                        break;
                    case StructTypeNode structType:
                        defaultValue = CreateStructValue(structType);
                        break;
                    default:
                        throw new NotImplementedException();
                }
                if (!isArray)
                {
                    return defaultValue;
                }
                else
                {
                    HLSLValue[] vals = new HLSLValue[arrayLength];
                    for (int i = 0; i < vals.Length; i++)
                    {
                        vals[i] = defaultValue.Copy();
                    }
                    return new ArrayValue(vals);
                }
            }
        }

        internal StructValue CreateStructValue(StructTypeNode structType)
        {
            Dictionary<string, HLSLValue> members = new Dictionary<string, HLSLValue>();
            foreach (var (kind, decl) in HLSLTypeUtils.GetStructFields(structType, context))
            {
                members[decl.Name] = GetVariableDeclarationInitialValue(kind, decl);
            }
            return new StructValue(context.GetQualifiedName(structType.Name.GetName()), members);
        }

        internal StructValue CreateStructValueFilledWith(StructTypeNode structType, NumericValue fillValue)
        {
            Dictionary<string, HLSLValue> members = new Dictionary<string, HLSLValue>();
            foreach (var (kind, decl) in HLSLTypeUtils.GetStructFields(structType, context))
            {
                HLSLValue fieldValue;
                switch (kind)
                {
                    case ScalarTypeNode scalarType:
                        fieldValue = fillValue.Cast(scalarType.Kind);
                        break;
                    case VectorTypeNode vectorType:
                        fieldValue = fillValue.Cast(vectorType.Kind).BroadcastToVector(vectorType.Dimension);
                        break;
                    case MatrixTypeNode matrixType:
                        fieldValue = fillValue.Cast(matrixType.Kind).BroadcastToMatrix(matrixType.FirstDimension, matrixType.SecondDimension);
                        break;
                    case UserDefinedNamedTypeNode namedType:
                        var nestedStruct = context.GetStruct(namedType.GetName());
                        fieldValue = CreateStructValueFilledWith(nestedStruct, fillValue);
                        break;
                    default:
                        fieldValue = GetVariableDeclarationInitialValue(kind, decl);
                        break;
                }
                members[decl.Name] = fieldValue;
            }
            return new StructValue(context.GetQualifiedName(structType.Name.GetName()), members);
        }

        // Visitor implementation
        protected override void DefaultVisit(HLSLSyntaxNode node)
        {
            if (node is ExpressionNode expr)
                expressionEvaluator.Visit(expr);
            else
                base.DefaultVisit(node);
        }

        public override void VisitVariableDeclarationStatementNode(VariableDeclarationStatementNode node)
        {
            bool isGroupshared = node.Modifiers.Contains(BindingModifier.Groupshared);
            if (isGroupshared)
            {
                if (!context.IsGlobalScope())
                    throw Error(node, "Groupshared variables can only be declared in global scope.");
                if (node.Declarators.Any(x => x.Initializer != null))
                    throw Error(node, "Groupshared variables cannot have initializers.");
            }

            // Inline struct definition: register the struct and its static methods before processing declarators.
            if (node.Kind is StructTypeNode inlineStruct)
            {
                context.AddStruct(inlineStruct.Name.GetName(), inlineStruct);
                foreach (var method in inlineStruct.Methods)
                {
                    if (method is FunctionDefinitionNode func && method.Modifiers.Contains(BindingModifier.Static))
                        context.AddFunction($"{inlineStruct.Name.GetName()}::{func.Name.GetName()}", func);
                }
            }

            foreach (VariableDeclaratorNode decl in node.Declarators)
            {
                context.AddVariable(decl.Name, GetVariableDeclarationInitialValue(node.Kind, decl), isGroupshared);
            }
        }

        public override void VisitIfStatementNode(IfStatementNode node)
        {
            ScalarValue boolCondValue = EvaluateScalar(node.Condition);

            bool[] perThreadCond = new bool[executionState.GetThreadCount()];

            context.PushScope();
            executionState.PushExecutionMask(ExecutionScope.Conditional);
            for (int threadIndex = 0; threadIndex < executionState.GetThreadCount(); threadIndex++)
            {
                perThreadCond[threadIndex] = Convert.ToBoolean(boolCondValue.Value.Get(threadIndex));
                if (perThreadCond[threadIndex] == false)
                    executionState.DisableThread(threadIndex);
            }

            if (executionState.IsAnyThreadActive())
                Visit(node.Body);

            executionState.PopExecutionMask();
            context.PopScope();

            context.PushScope();
            executionState.PushExecutionMask(ExecutionScope.Conditional);
            for (int threadIndex = 0; threadIndex < executionState.GetThreadCount(); threadIndex++)
            {
                if (perThreadCond[threadIndex] == true)
                    executionState.DisableThread(threadIndex);
            }

            if (node.ElseClause != null)
            {
                if (executionState.IsAnyThreadActive())
                    Visit(node.ElseClause);
            }

            executionState.PopExecutionMask();
            context.PopScope();
        }

        public override void VisitSwitchStatementNode(SwitchStatementNode node)
        {
            NumericValue expr = EvaluateNumeric(node.Expression);
            context.PushScope();
            executionState.PushExecutionMask(ExecutionScope.Conditional);

            foreach (var clause in node.Clauses)
            {
                executionState.PushExecutionMask(ExecutionScope.Block);

                // For each thread, check if a label matches
                bool[] pass = new bool[executionState.GetThreadCount()];
                foreach (var label in clause.Labels)
                {
                    if (label is SwitchDefaultLabelNode)
                    {
                        // Default matches everything
                        Array.Fill(pass, true);
                    }
                    else if (label is SwitchCaseLabelNode caseLabel)
                    {
                        // Do comparison for each thread
                        var labelValue = EvaluateNumeric(caseLabel.Value);
                        var cond = labelValue == expr;
                        
                        for (int threadIndex = 0; threadIndex < executionState.GetThreadCount(); threadIndex++)
                        {
                            var boolCondValue = cond.GetThreadValue(threadIndex);
                            if (cond is ScalarValue sv)
                                pass[threadIndex] |= Convert.ToBoolean(boolCondValue);
                            else
                                pass[threadIndex] |= boolCondValue is object[] components && components.All(x => Convert.ToBoolean(x));
                        }
                    }
                }

                // Disable all threads which didn't pass
                for (int threadIndex = 0; threadIndex < executionState.GetThreadCount(); threadIndex++)
                {
                    if (!pass[threadIndex])
                        executionState.DisableThread(threadIndex);
                }

                // Run body
                foreach (var stmt in clause.Statements)
                {
                    if (stmt is BreakStatementNode)
                        break;
                    if (executionState.IsAnyThreadActive())
                        Visit(stmt);
                }

                // Disable all threads that passed for the rest of the switch statement
                for (int threadIndex = 0; threadIndex < executionState.GetThreadCount(); threadIndex++)
                {
                    if (pass[threadIndex])
                        executionState.KillThreadInConditional(threadIndex);
                }
                executionState.PopExecutionMask();
            }

            executionState.PopExecutionMask();
            context.PopScope();
        }

        public override void VisitWhileStatementNode(WhileStatementNode node)
        {
            context.PushScope();
            executionState.PushExecutionMask(ExecutionScope.Loop);
            bool anyRunning = true;
            while (anyRunning)
            {
                anyRunning = false;

                ScalarValue boolCondValue = EvaluateScalar(node.Condition);

                for (int threadIndex = 0; threadIndex < executionState.GetThreadCount(); threadIndex++)
                {
                    if (!executionState.IsThreadActive(threadIndex))
                        continue;

                    bool threadCond = Convert.ToBoolean(boolCondValue.Value.Get(threadIndex));
                    if (!threadCond)
                        executionState.DisableThread(threadIndex);
                    anyRunning |= threadCond;
                }

                if (executionState.IsAnyThreadActive())
                    Visit(node.Body);
                executionState.ResumeSuspendedThreadsInLoop();
            }
            executionState.PopExecutionMask();
            context.PopScope();
        }

        public override void VisitDoWhileStatementNode(DoWhileStatementNode node)
        {
            context.PushScope();
            executionState.PushExecutionMask(ExecutionScope.Loop);
            bool anyRunning = true;
            while (anyRunning)
            {
                anyRunning = false;

                if (executionState.IsAnyThreadActive())
                    Visit(node.Body);
                executionState.ResumeSuspendedThreadsInLoop();

                ScalarValue boolCondValue = EvaluateScalar(node.Condition);

                for (int threadIndex = 0; threadIndex < executionState.GetThreadCount(); threadIndex++)
                {
                    if (!executionState.IsThreadActive(threadIndex))
                        continue;

                    bool threadCond = Convert.ToBoolean(boolCondValue.Value.Get(threadIndex));
                    if (!threadCond)
                        executionState.DisableThread(threadIndex);
                    anyRunning |= threadCond;
                }
            }
            executionState.PopExecutionMask();
            context.PopScope();
        }

        public override void VisitForStatementNode(ForStatementNode node)
        {
            // For loops are weird in HLSL, they declare a variable in outer scope
            if (node.Declaration != null)
                Visit(node.Declaration);
            else if (node.Initializer != null)
                Visit(node.Initializer);

            context.PushScope();
            executionState.PushExecutionMask(ExecutionScope.Loop);
            bool anyRunning = true;
            while (anyRunning)
            {
                anyRunning = false;

                ScalarValue boolCondValue = EvaluateScalar(node.Condition);

                for (int threadIndex = 0; threadIndex < executionState.GetThreadCount(); threadIndex++)
                {
                    if (!executionState.IsThreadActive(threadIndex))
                        continue;

                    bool threadCond = Convert.ToBoolean(boolCondValue.Value.Get(threadIndex));
                    if (!threadCond)
                        executionState.DisableThread(threadIndex);
                    anyRunning |= threadCond;
                }

                if (executionState.IsAnyThreadActive())
                    Visit(node.Body);
                executionState.ResumeSuspendedThreadsInLoop();

                Visit(node.Increment);
            }
            executionState.PopExecutionMask();
            context.PopScope();
        }

        public override void VisitFunctionDefinitionNode(FunctionDefinitionNode node)
        {
            context.AddFunction(node.Name.GetName(), node);
        }

        public override void VisitTypedefNode(TypedefNode node)
        {
            foreach (var toName in node.ToNames)
                context.AddTypeAlias(toName.GetName(), node.FromType);
        }

        public override void VisitStructDefinitionNode(StructDefinitionNode node)
        {
            context.AddStruct(node.StructType.Name.GetName(), node.StructType);

            foreach (var method in node.StructType.Methods)
            {
                if (method is FunctionDefinitionNode func)
                {
                    if (method.Modifiers.Contains(BindingModifier.Static))
                    {
                        context.AddFunction($"{node.StructType.Name.GetName()}::{func.Name.GetName()}", func);
                    }
                }
            }
        }

        public override void VisitNamespaceNode(NamespaceNode node)
        {
            context.EnterNamespace(node.Name.GetName());
            VisitMany(node.Declarations);
            context.ExitNamespace();
        }

        public override void VisitReturnStatementNode(ReturnStatementNode node)
        {
            if (node.Expression != null)
            {
                var returnValue = expressionEvaluator.Visit(node.Expression);

                // If we are in varying control flow, vectorize the value so we can splat each active thread.
                if (executionState.IsVaryingExecution())
                    returnValue = HLSLValueUtils.Vectorize(returnValue, executionState.GetThreadCount());

                // For each active thread, kill the thread and splat the return.
                for (int threadIndex = 0; threadIndex < executionState.GetThreadCount(); threadIndex++)
                {
                    if (executionState.IsThreadActive(threadIndex))
                    {
                        context.SetReturn(threadIndex, returnValue);
                        executionState.KillThreadInFunction(threadIndex);
                    }
                }
            }
            else
                throw Error(node, "Error evaluating return statement.");
        }

        public override void VisitContinueStatementNode(ContinueStatementNode node)
        {
            for (int threadIndex = 0; threadIndex < executionState.GetThreadCount(); threadIndex++)
            {
                if (executionState.IsThreadActive(threadIndex))
                {
                    executionState.SuspendThreadInLoop(threadIndex);
                }
            }
        }

        public override void VisitBreakStatementNode(BreakStatementNode node)
        {
            for (int threadIndex = 0; threadIndex < executionState.GetThreadCount(); threadIndex++)
            {
                if (executionState.IsThreadActive(threadIndex))
                {
                    executionState.KillThreadInLoop(threadIndex);
                }
            }
        }

        public override void VisitDiscardStatementNode(DiscardStatementNode node)
        {
            for (int threadIndex = 0; threadIndex < executionState.GetThreadCount(); threadIndex++)
            {
                if (executionState.IsThreadActive(threadIndex))
                    executionState.KillThreadGlobally(threadIndex);
            }
        }

        public override void VisitBlockNode(BlockNode node)
        {
            context.PushScope();
            VisitMany(node.Statements);
            context.PopScope();
        }

        public override void VisitExpressionStatementNode(ExpressionStatementNode node)
        {
            expressionEvaluator.Visit(node.Expression);
        }

        public override void VisitConstantBufferNode(ConstantBufferNode node)
        {
            VisitMany(node.Declarations);
        }
    }
}
