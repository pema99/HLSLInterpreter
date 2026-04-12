using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityShaderParser.Common;
using UnityShaderParser.HLSL;

namespace UnityShaderParser.Test
{
    // TODO: Put this functionality as recursive functions in interpreter rather than using SyntaxVisitor
    public class HLSLExpressionEvaluator : HLSLSyntaxVisitor<HLSLValue>
    {
        protected HLSLInterpreter interpreter;
        protected HLSLInterpreterContext context;
        protected HLSLExecutionState executionState;

        protected Dictionary<string, Func<HLSLExecutionState, ExpressionNode[], HLSLValue>> callbacks = new Dictionary<string, Func<HLSLExecutionState, ExpressionNode[], HLSLValue>>();

        public HLSLExpressionEvaluator(HLSLInterpreter interpreter, HLSLInterpreterContext context, HLSLExecutionState executionState)
        {
            this.interpreter = interpreter;
            this.context = context;
            this.executionState = executionState;
        }

        // Public API
        public void AddCallback(string name, Func<HLSLExecutionState, ExpressionNode[], HLSLValue> callback) => callbacks.Add(name, callback);
        public void RemoveCallback(string name) => callbacks.Remove(name);
        public IEnumerable<KeyValuePair<string, Func<HLSLExecutionState, ExpressionNode[], HLSLValue>>> GetCallbacks() => callbacks;

        public HLSLValue CallFunction(string name, params HLSLValue[] args)
        {
            FunctionDefinitionNode func = context.GetFunction(this, name, args);
            if (func != null)
            {
                if (args.Length != func.Parameters.Count)
                    throw Error($"Argument count mismatch in call to '{name}'.");

                // Enter namespace
                string[] namespaces = null;
                if (name.Contains("::"))
                {
                    namespaces = name.Split("::");
                    for (int i = 0; i < namespaces.Length - 1; i++)
                        context.EnterNamespace(namespaces[i]);
                }

                // Call function
                context.PushScope(isFunction: true);
                context.PushReturn();
                executionState.PushExecutionMask(ExecutionScope.Function);

                for (int i = 0; i < func.Parameters.Count; i++)
                {
                    var param = func.Parameters[i];
                    var declarator = param.Declarator;
                    context.AddVariable(declarator.Name, HLSLValueUtils.CastForParameter(this, args[i], param.ParamType));
                }
                interpreter.Visit(func.Body);

                executionState.PopExecutionMask();
                context.PopScope();

                // Exit namespace
                if (namespaces != null)
                {
                    for (int i = 0; i < namespaces.Length - 1; i++)
                        context.ExitNamespace();
                }

                return context.PopReturn();
            }
            
            // Try to invoke intrinsics
            if (HLSLIntrinsics.TryInvokeIntrinsic(executionState, name, args, out HLSLValue result))
                return result;

            if (HLSLIntrinsics.IsUnsupportedIntrinsic(name))
                throw Error($"Intrinsic function '{name}' is not supported.");

            // Check if name is a typedef alias for a numeric type used as a constructor.
            if (context.TryLookupTypeAlias(name, out TypeNode aliasedType) && aliasedType is NumericTypeNode numericAliasType)
            {
                foreach (var arg in  args)
                {
                    if (arg is not NumericValue)
                        Error("Expected numeric value arguments to constructor.");
                }
                return ConstructNumericValue(numericAliasType, args.Select(a => (NumericValue)a).ToArray());
            }

            // Try as an implicit this.method() call (e.g. calling an inherited method from within a method body)
            if (context.GetReference("this")?.Get() is StructValue thisStruct)
            {
                if (TryFindMethod(thisStruct.Name, name, out var method))
                    return InvokeMethod(thisStruct, method, args);
            }

            throw Error($"Unknown function '{name}' called.");
        }

        public TypeNode ResolveType(TypeNode type) => context.ResolveType(type);

        // Helpers
        private static Exception Error(HLSLSyntaxNode node, string message)
        {
            return new Exception($"Error at line {node.Span.Start.Line}, column {node.Span.Start.Column}: {message}");
        }

        private static Exception Error(string message)
        {
            return new Exception($"Error: {message}");
        }

        private NumericValue EvaluateNumeric(ExpressionNode node, ScalarType type = ScalarType.Void)
        {
            var value = Visit(node);
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
            var value = Visit(node);
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

        private bool TryGetLValueReference(ExpressionNode node, out ReferenceValue reference)
            => TryGetLValueReference(node, out reference, out _);

        private bool TryGetLValueReference(ExpressionNode node, out ReferenceValue reference, out bool isGroupshared)
        {
            reference = null;
            isGroupshared = false;

            if (node is NamedExpressionNode named)
            {
                isGroupshared = context.IsGroupShared(named.GetName());
                if (isGroupshared)
                {
                    reference = new ReferenceValue(
                        () => context.GetVariable(named.GetName()),
                        newValue =>
                        {
                            for (int threadIndex = 0; threadIndex < executionState.GetThreadCount(); threadIndex++)
                            {
                                if (!executionState.IsThreadActive(threadIndex)) continue;
                                context.SetVariable(named.GetName(), HLSLValueUtils.Scalarize(newValue, threadIndex));
                            }
                        });
                }
                else
                {
                    reference = context.GetReference(named.GetName());
                }
                return reference != null;
            }

            if (node is FieldAccessExpressionNode fieldAccess)
            {
                if (!TryGetLValueReference(fieldAccess.Target, out var parentRef, out isGroupshared))
                    return false;
                string field = fieldAccess.Name.Identifier;
                if (parentRef.Get() is StructValue)
                {
                    if (isGroupshared)
                    {
                        reference = new ReferenceValue(
                            () => ((StructValue)parentRef.Get()).Members[field],
                            newValue =>
                            {
                                for (int threadIndex = 0; threadIndex < executionState.GetThreadCount(); threadIndex++)
                                {
                                    if (!executionState.IsThreadActive(threadIndex)) continue;
                                    ((StructValue)parentRef.Get()).Members[field] = HLSLValueUtils.Scalarize(newValue, threadIndex);
                                }
                            });
                    }
                    else
                    {
                        reference = new ReferenceValue(
                            () => ((StructValue)parentRef.Get()).Members[field],
                            newValue => ((StructValue)parentRef.Get()).Members[field] = newValue);
                    }
                    return true;
                }
                if (parentRef.Get() is VectorValue)
                {
                    reference = new ReferenceValue(
                        () => ((VectorValue)parentRef.Get()).Swizzle(field),
                        val => parentRef.Set(((VectorValue)parentRef.Get()).SwizzleAssign(field, (NumericValue)val)));
                    return true;
                }
                if (parentRef.Get() is MatrixValue)
                {
                    reference = new ReferenceValue(
                        () => ((MatrixValue)parentRef.Get()).Swizzle(field),
                        val => parentRef.Set(((MatrixValue)parentRef.Get()).SwizzleAssign(field, (NumericValue)val)));
                    return true;
                }
            }

            if (node is ElementAccessExpressionNode elementAccess)
            {
                if (!TryGetLValueReference(elementAccess.Target, out var parentRef, out isGroupshared))
                    return false;
                var indexVal = EvaluateScalar(elementAccess.Index);
                if (parentRef.Get() is ArrayValue)  { reference = GetArrayElementLValue(parentRef, indexVal, isGroupshared);  return true; }
                if (parentRef.Get() is VectorValue) { reference = GetVectorElementLValue(parentRef, indexVal, isGroupshared); return true; }
                if (parentRef.Get() is MatrixValue) { reference = GetMatrixElementLValue(parentRef, indexVal, isGroupshared); return true; }
            }

            return false;
        }

        private ReferenceValue GetArrayElementLValue(ReferenceValue parentRef, ScalarValue indexVal, bool isGroupshared = false)
        {
            return new ReferenceValue(
                () => {
                    var array = (ArrayValue)parentRef.Get();
                    if (indexVal.Value.IsUniform)
                    {
                        return array.Values[Convert.ToInt32(indexVal.Value.UniformValue)];
                    }
                    else
                    {
                        int threadCount = executionState.GetThreadCount();
                        HLSLValue result = HLSLValueUtils.Vectorize(array.Values[Convert.ToInt32(indexVal.Value.Get(0))], threadCount);
                        for (int threadIndex = 0; threadIndex < threadCount; threadIndex++)
                        {
                            int index = Convert.ToInt32(indexVal.Value.Get(threadIndex));
                            result = HLSLValueUtils.SetThreadValue(result, threadIndex, array.Values[index]);
                        }
                        return result;
                    }
                },
                val => {
                    var array = (ArrayValue)parentRef.Get();
                    if (isGroupshared)
                    {
                        for (int threadIndex = 0; threadIndex < executionState.GetThreadCount(); threadIndex++)
                        {
                            if (!executionState.IsThreadActive(threadIndex)) continue;
                            int index = Convert.ToInt32(indexVal.Value.Get(threadIndex));
                            array.Values[index] = HLSLValueUtils.Scalarize(val, threadIndex);
                        }
                    }
                    else if (indexVal.Value.IsUniform)
                    {
                        int index = Convert.ToInt32(indexVal.Value.UniformValue);
                        array.Values[index] = val;
                    }
                    else
                    {
                        int threadCount = executionState.GetThreadCount();
                        for (int threadIndex = 0; threadIndex < threadCount; threadIndex++)
                        {
                            int index = Convert.ToInt32(indexVal.Value.Get(threadIndex));
                            var elem = HLSLValueUtils.Vectorize(array.Values[index], threadCount);
                            array.Values[index] = HLSLValueUtils.SetThreadValue(elem, threadIndex, HLSLValueUtils.Scalarize(val, threadIndex));
                        }
                    }
                });
        }

        private ReferenceValue GetVectorElementLValue(ReferenceValue parentRef, ScalarValue indexVal, bool isGroupshared = false)
        {
            return new ReferenceValue(
                () => {
                    var vec = (VectorValue)parentRef.Get();
                    if (indexVal.Value.IsUniform)
                    {
                        return vec[Convert.ToInt32(indexVal.Value.UniformValue)];
                    }
                    else
                    {
                        int threadCount = executionState.GetThreadCount();
                        HLSLValue result = HLSLValueUtils.Vectorize(vec[Convert.ToInt32(indexVal.Value.Get(0))], threadCount);
                        for (int threadIndex = 0; threadIndex < threadCount; threadIndex++)
                        {
                            int channel = Convert.ToInt32(indexVal.Value.Get(threadIndex));
                            result = HLSLValueUtils.SetThreadValue(result, threadIndex, vec[channel]);
                        }
                        return result;
                    }
                },
                val => {
                    var vec = (VectorValue)parentRef.Get();
                    if (isGroupshared)
                    {
                        for (int threadIndex = 0; threadIndex < executionState.GetThreadCount(); threadIndex++)
                        {
                            if (!executionState.IsThreadActive(threadIndex)) continue;
                            int channel = Convert.ToInt32(indexVal.Value.Get(threadIndex));
                            vec = vec.ChannelAssign(channel, (NumericValue)HLSLValueUtils.Scalarize(val, threadIndex));
                        }
                        parentRef.Set(vec);
                    }
                    else if (indexVal.Value.IsUniform)
                    {
                        parentRef.Set(vec.ChannelAssign(Convert.ToInt32(indexVal.Value.UniformValue), (NumericValue)val));
                    }
                    else
                    {
                        vec = (VectorValue)vec.Vectorize(executionState.GetThreadCount());
                        for (int threadIndex = 0; threadIndex < executionState.GetThreadCount(); threadIndex++)
                        {
                            int channel = Convert.ToInt32(indexVal.Value.Get(threadIndex));
                            vec = (VectorValue)HLSLValueUtils.SetThreadValue(vec, threadIndex, vec.ChannelAssign(channel, (NumericValue)val));
                        }
                        parentRef.Set(vec);
                    }
                });
        }

        private ReferenceValue GetMatrixElementLValue(ReferenceValue parentRef, ScalarValue indexVal, bool isGroupshared = false)
        {
            int columnCount = ((MatrixValue)parentRef.Get()).Columns;
            return new ReferenceValue(
                () => {
                    var matrix = (MatrixValue)parentRef.Get();
                    if (indexVal.Value.IsUniform)
                    {
                        int row = Convert.ToInt32(indexVal.Value.UniformValue);
                        return VectorValue.FromScalars(matrix.ToScalars().Skip(row * columnCount).Take(columnCount).ToArray());
                    }
                    else
                    {
                        int threadCount = executionState.GetThreadCount();
                        var expanded = matrix.ThreadCount < threadCount ? (MatrixValue)matrix.Vectorize(threadCount) : matrix;
                        return new VectorValue(matrix.Type, expanded.Values.MapThreads((threadData, threadIndex) =>
                        {
                            int row = Convert.ToInt32(indexVal.Value.Get(threadIndex));
                            var rowElements = new object[columnCount];
                            Array.Copy(threadData, row * columnCount, rowElements, 0, columnCount);
                            return rowElements;
                        }));
                    }
                },
                val => {
                    var matrix = (MatrixValue)parentRef.Get();
                    if (isGroupshared)
                    {
                        for (int threadIndex = 0; threadIndex < executionState.GetThreadCount(); threadIndex++)
                        {
                            if (!executionState.IsThreadActive(threadIndex)) continue;
                            int row = Convert.ToInt32(indexVal.Value.Get(threadIndex));
                            var rowData = (object[])((NumericValue)HLSLValueUtils.Scalarize(val, threadIndex)).GetThreadValue(0);
                            var matData = (object[])matrix.Values.Get(0).Clone();
                            Array.Copy(rowData, 0, matData, row * columnCount, columnCount);
                            matrix = new MatrixValue(matrix.Type, matrix.Rows, columnCount, new HLSLRegister<object[]>(matData));
                        }
                        parentRef.Set(matrix);
                    }
                    else
                    {
                        int threadCount = Math.Max(matrix.ThreadCount, ((NumericValue)val).ThreadCount);
                        var expanded = (MatrixValue)matrix.Vectorize(threadCount);
                        parentRef.Set(new MatrixValue(matrix.Type, matrix.Rows, columnCount, expanded.Values.MapThreads((threadData, threadIndex) =>
                        {
                            int row = Convert.ToInt32(indexVal.Value.Get(threadIndex));
                            var matData = (object[])threadData.Clone();
                            Array.Copy((object[])((NumericValue)val).GetThreadValue(threadIndex), 0, matData, row * columnCount, columnCount);
                            return matData;
                        })));
                    }
                });
        }

        private HLSLValue ConstructNumericValue(NumericTypeNode type, NumericValue[] args)
        {
            int maxThreadCount = 1;
            bool anyUniform = false;
            for (int i = 0; i < args.Length; i++)
            {
                maxThreadCount = Math.Max(maxThreadCount, args[i].ThreadCount);
                anyUniform |= args[i].ThreadCount == 1;
            }
            if (anyUniform && maxThreadCount > 1)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    args[i] = args[i].Vectorize(maxThreadCount);
                }
            }

            object[][] lanes = new object[maxThreadCount][];
            for (int threadIdx = 0; threadIdx < maxThreadCount; threadIdx++)
            {
                List<object> flattened = new List<object>();
                foreach (var numeric in args)
                {
                    if (numeric is ScalarValue scalar) flattened.Add(scalar.Value.Get(threadIdx));
                    if (numeric is VectorValue vector) flattened.AddRange(vector.Values.Get(threadIdx));
                }
                for (int i = 0; i < flattened.Count; i++)
                {
                    flattened[i] = HLSLValueUtils.CastNumeric(type.Kind, flattened[i]);
                }
                lanes[threadIdx] = flattened.ToArray();
            }

            switch (type)
            {
                case ScalarTypeNode st:
                    if (maxThreadCount == 1) return new ScalarValue(st.Kind, HLSLValueUtils.MakeScalarSGPR(lanes[0][0]));
                    else return new ScalarValue(st.Kind, HLSLValueUtils.MakeScalarVGPR(lanes.Select(l => l[0])));
                case VectorTypeNode _:
                case GenericVectorTypeNode _:
                    if (maxThreadCount == 1) return new VectorValue(type.Kind, new HLSLRegister<object[]>(lanes[0]));
                    else return new VectorValue(type.Kind, new HLSLRegister<object[]>(lanes));
                case MatrixTypeNode matrix:
                    if (maxThreadCount == 1) return new MatrixValue(type.Kind, matrix.FirstDimension, matrix.SecondDimension, new HLSLRegister<object[]>(lanes[0]));
                    else return new MatrixValue(type.Kind, matrix.FirstDimension, matrix.SecondDimension, new HLSLRegister<object[]>(lanes));
                case GenericMatrixTypeNode genMatrix:
                    var d1 = Visit(genMatrix.FirstDimension) as ScalarValue;
                    var d2 = Visit(genMatrix.SecondDimension) as ScalarValue;
                    if (maxThreadCount == 1) return new MatrixValue(type.Kind, Convert.ToInt32(d1.Value), Convert.ToInt32(d2.Value), new HLSLRegister<object[]>(lanes[0]));
                    else return new MatrixValue(type.Kind, Convert.ToInt32(d1.Value), Convert.ToInt32(d2.Value), new HLSLRegister<object[]>(lanes));
                default:
                    throw Error($"Unknown numeric constructor type.");
            }
        }

        private HLSLValue SplatActiveThreadValues(HLSLValue prevValue, HLSLValue value)
        {
            HLSLValue newValue = HLSLValueUtils.Vectorize(prevValue, executionState.GetThreadCount());
            for (int threadIndex = 0; threadIndex < executionState.GetThreadCount(); threadIndex++)
            {
                if (executionState.IsThreadActive(threadIndex))
                {
                    newValue = HLSLValueUtils.SetThreadValue(newValue, threadIndex, value);
                }
            }
            return newValue;
        }

        private HLSLValue SetValueSimpleNamed(string name, HLSLValue value)
        {
            if (executionState.IsVaryingExecution())
            {
                HLSLValue curr;
                if (context.TryGetVariable(name, out var variable) &&
                    variable is ReferenceValue reference)
                {
                    curr = reference.Get();
                }
                else
                {
                    curr = context.GetVariable(name);
                }
                value = SplatActiveThreadValues(curr, value);
            }

            {
                if (context.TryGetVariable(name, out var variable) &&
                    variable is ReferenceValue reference)
                {
                    reference.Set(value);
                }
                else
                {
                    context.SetVariable(name, value);
                }
            }

            return value;
        }

        private bool TryFindMethod(string structName, string methodName, out FunctionDefinitionNode function)
        {
            function = null;

            var structDef = context.GetStruct(structName);
            if (structDef == null)
                return false;

            foreach (var method in structDef.Methods)
            {
                if (method is FunctionDefinitionNode func && method.Name.GetName() == methodName)
                {
                    function = func;
                    return true;
                }
            }

            foreach (var baseTypeName in structDef.Inherits)
            {
                string baseName = baseTypeName.GetName();
                if (TryFindMethod(baseName, methodName, out function))
                    return true;

                // Couldn't find method, try with qualified name.
                if (structName.Contains("::"))
                {
                    string namespacePrefix = structName.Substring(0, structName.LastIndexOf("::"));
                    if (TryFindMethod($"{namespacePrefix}::{baseName}", methodName, out function))
                        return true;
                }
            }

            return false;
        }

        private HLSLValue InvokeMethod(StructValue str, FunctionDefinitionNode method, HLSLValue[] args)
        {
            if (args.Length != method.Parameters.Count)
                throw Error($"Argument count mismatch in call to '{method.Name.GetName()}'.");

            context.PushScope(isFunction: true);
            context.PushReturn();
            executionState.PushExecutionMask(ExecutionScope.Function);

            // If this is an instance method, push the fields as local variables, alongside 'this'.
            if (!method.Modifiers.Contains(BindingModifier.Static))
            {
                foreach (string field in str.Members.Keys)
                {
                    context.AddVariable(field, new ReferenceValue(
                        () => str.Members[field],
                        val => str.Members[field] = val));
                }

                context.AddVariable("this", new ReferenceValue(
                    () => str,
                    newVal =>
                    {
                        if (newVal is StructValue newStruct)
                        {
                            foreach (var kvp in newStruct.Members)
                            {
                                str.Members[kvp.Key] = kvp.Value;
                            }
                        }
                    }));
            }

            for (int i = 0; i < method.Parameters.Count; i++)
            {
                var param = method.Parameters[i];
                context.AddVariable(param.Declarator.Name, HLSLValueUtils.CastForParameter(this, args[i], param.ParamType));
            }
            interpreter.Visit(method.Body);

            executionState.PopExecutionMask();
            context.PopScope();

            return context.PopReturn();
        }

        // Visit implementation
        protected override HLSLValue DefaultVisit(HLSLSyntaxNode node)
        {
            throw new Exception($"{nameof(HLSLExpressionEvaluator)} should only be used to evaluate expressions.");
        }

        public override HLSLValue VisitQualifiedIdentifierExpressionNode(QualifiedIdentifierExpressionNode node)
        {
            if (context.TryGetVariable(node.GetName(), out var variable))
            {
                if (variable is ReferenceValue reference)
                    return reference.Get();
                else
                    return variable;
            }
            else
            {
                throw Error(node, $"Use of unknown variable '{node.GetName()}'.");
            }
        }
        
        public override HLSLValue VisitIdentifierExpressionNode(IdentifierExpressionNode node)
        {
            if (context.TryGetVariable(node.GetName(), out var variable))
            {
                if (variable is ReferenceValue reference)
                    return reference.Get();
                else
                    return variable;
            }
            else
            {
                throw Error(node, $"Use of unknown variable '{node.GetName()}'.");
            }
        }
        
        public override HLSLValue VisitLiteralExpressionNode(LiteralExpressionNode node)
        {
            switch (node.Kind)
            {
                case LiteralKind.String:
                    return new ScalarValue(ScalarType.String, new HLSLRegister<object>(node.Lexeme));
                case LiteralKind.Float:
                    string floatLexeme = node.Lexeme;
                    if (floatLexeme.EndsWith('f'))
                        floatLexeme = floatLexeme.Substring(0, node.Lexeme.Length - 1);
                    if (float.TryParse(floatLexeme, NumberStyles.Any, CultureInfo.InvariantCulture, out float parsedFloat))
                        return new ScalarValue(ScalarType.Float, new HLSLRegister<object>(parsedFloat));
                    else
                        throw Error(node, $"Invalid float literal '{node.Lexeme}'.");
                case LiteralKind.Integer:
                    if (node.Lexeme.EndsWith('u'))
                    {
                        string lexeme = node.Lexeme.Substring(0, node.Lexeme.Length - 1);
                        if (uint.TryParse(lexeme, NumberStyles.Any, CultureInfo.InvariantCulture, out uint parsedUint))
                            return new ScalarValue(ScalarType.Uint, new HLSLRegister<object>(parsedUint));
                        else if (lexeme.StartsWith("0x") && uint.TryParse(lexeme.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint parsedHexUint))
                            return new ScalarValue(ScalarType.Uint, new HLSLRegister<object>(parsedHexUint));
                    }
                    else
                    {
                        if (int.TryParse(node.Lexeme, NumberStyles.Any, CultureInfo.InvariantCulture, out int parsedInt))
                            return new ScalarValue(ScalarType.Int, new HLSLRegister<object>(parsedInt));
                        else if (node.Lexeme.StartsWith("0x") && int.TryParse(node.Lexeme.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int parsedHexInt))
                            return new ScalarValue(ScalarType.Int, new HLSLRegister<object>(parsedHexInt));
                    }
                    throw Error(node, $"Invalid integer literal '{node.Lexeme}'.");
                case LiteralKind.Character:
                    if (char.TryParse(node.Lexeme, out char parsedChar))
                        return new ScalarValue(ScalarType.Char, new HLSLRegister<object>(parsedChar));
                    else
                        throw Error(node, $"Invalid character literal '{node.Lexeme}'.");
                case LiteralKind.Boolean:
                    if (bool.TryParse(node.Lexeme, out bool parsedBool))
                        return new ScalarValue(ScalarType.Bool, new HLSLRegister<object>(parsedBool));
                    else
                        throw Error(node, $"Invalid boolean literal '{node.Lexeme}'.");
                case LiteralKind.Null:
                    return ScalarValue.Null;
                default:
                    throw Error(node, $"Unknown literal '{node.Lexeme}'.");
            }
        }
        
        public override HLSLValue VisitAssignmentExpressionNode(AssignmentExpressionNode node)
        {
            var left = Visit(node.Left);
            var right = Visit(node.Right);
            right = HLSLValueUtils.CastForAssignment(left, right);

            // TODO: StructuredBuffer/Resource writes
            HLSLValue SetValue(HLSLValue value)
            {
                // lhs = rhs
                if (node.Left is NamedExpressionNode named &&
                    !context.IsGroupShared(named.GetName()))
                {
                    return SetValueSimpleNamed(named.GetName(), value);
                }
                // lhs.member = rhs
                else if (node.Left is FieldAccessExpressionNode fieldAccess &&
                    fieldAccess.Target is NamedExpressionNode namedTarget &&
                    !context.IsGroupShared(namedTarget.GetName()))
                {
                    string name = namedTarget.GetName();
                    var variable = context.GetVariable(name);

                    if (variable is VectorValue vec)
                    {
                        return SetValueSimpleNamed(name, vec.SwizzleAssign(fieldAccess.Name, (NumericValue)value));
                    }
                    else if (variable is ReferenceValue refVec && refVec.Get() is VectorValue vecInner)
                    {
                        return SetValueSimpleNamed(name, vecInner.SwizzleAssign(fieldAccess.Name, (NumericValue)value));
                    }
                    else if (variable is MatrixValue mat)
                    {
                        return SetValueSimpleNamed(name, mat.SwizzleAssign(fieldAccess.Name, (NumericValue)value));
                    }
                    else if (variable is ReferenceValue refMat && refMat.Get() is MatrixValue matInner)
                    {
                        return SetValueSimpleNamed(name, matInner.SwizzleAssign(fieldAccess.Name, (NumericValue)value));
                    }
                    else
                    {
                        var structVal = (StructValue)variable;
                        if (executionState.IsVaryingExecution())
                            value = SplatActiveThreadValues(structVal.Members[fieldAccess.Name.Identifier], value);
                        structVal.Members[fieldAccess.Name.Identifier] = value;
                        return value;
                    }
                }
                // This fallback case can handle everything, but we keep the above branches for performance.
                else if (TryGetLValueReference(node.Left, out var lvalRef))
                {
                    if (executionState.IsVaryingExecution())
                        value = SplatActiveThreadValues(lvalRef.Get(), value);
                    lvalRef.Set(value);
                    return value;
                }
                else
                    throw Error(node, $"Invalid assignment.");
            }

            var leftNum = left as NumericValue;
            var rightNum = right as NumericValue;
            switch (node.Operator)
            {
                case OperatorKind.Assignment:
                    return SetValue(right.Copy());
                case OperatorKind.PlusAssignment:
                    return SetValue(leftNum + rightNum);
                case OperatorKind.MinusAssignment:
                    return SetValue(leftNum - rightNum);
                case OperatorKind.MulAssignment:
                    return SetValue(leftNum * rightNum);
                case OperatorKind.DivAssignment:
                    return SetValue(leftNum / rightNum);
                case OperatorKind.ModAssignment:
                    return SetValue(leftNum % rightNum);
                case OperatorKind.ShiftLeftAssignment:
                    return SetValue(HLSLOperators.BitSHL(leftNum, rightNum));
                case OperatorKind.ShiftRightAssignment:
                    return SetValue(HLSLOperators.BitSHR(leftNum, rightNum));
                case OperatorKind.BitwiseAndAssignment:
                    return SetValue(leftNum & rightNum);
                case OperatorKind.BitwiseXorAssignment:
                    return SetValue(leftNum ^ rightNum);
                case OperatorKind.BitwiseOrAssignment:
                    return SetValue(leftNum | rightNum);
            }

            throw Error(node, $"Invalid assignment.");
        }
        
        public override HLSLValue VisitBinaryExpressionNode(BinaryExpressionNode node)
        {
            if (node.Operator == OperatorKind.Compound)
            {
                Visit(node.Left);
                return Visit(node.Right);
            }
            else
            {
                NumericValue nl = EvaluateNumeric(node.Left);
                NumericValue nr = EvaluateNumeric(node.Right);

                switch (node.Operator)
                {
                    case OperatorKind.LogicalOr: return HLSLOperators.BoolOr(nl, nr);
                    case OperatorKind.LogicalAnd: return HLSLOperators.BoolAnd(nl, nr);
                    case OperatorKind.BitwiseOr: return nl | nr;
                    case OperatorKind.BitwiseAnd: return nl & nr;
                    case OperatorKind.BitwiseXor: return nl ^ nr;
                    case OperatorKind.Equals: return nl == nr;
                    case OperatorKind.NotEquals: return nl != nr;
                    case OperatorKind.LessThan: return nl < nr;
                    case OperatorKind.LessThanOrEquals: return nl <= nr;
                    case OperatorKind.GreaterThan: return nl > nr;
                    case OperatorKind.GreaterThanOrEquals: return nl >= nr;
                    case OperatorKind.ShiftLeft: return HLSLOperators.BitSHL(nl, nr);
                    case OperatorKind.ShiftRight: return HLSLOperators.BitSHR(nl, nr);
                    case OperatorKind.Plus: return nl + nr;
                    case OperatorKind.Minus: return nl - nr;
                    case OperatorKind.Mul: return nl * nr;
                    case OperatorKind.Div: return nl / nr;
                    case OperatorKind.Mod: return nl % nr;
                    default:
                        throw Error(node, $"Unexpected operator '{PrintingUtil.GetEnumName(node.Operator)}' in binary expression.");
                }
            }
        }
        
        public override HLSLValue VisitCompoundExpressionNode(CompoundExpressionNode node)
        {
            Visit(node.Left);
            return Visit(node.Right);
        }
        
        public override HLSLValue VisitPrefixUnaryExpressionNode(PrefixUnaryExpressionNode node)
        {
            // Special case for negative to handle INT_MIN
            if (node.Operator == OperatorKind.Minus && node.Expression is LiteralExpressionNode literal && literal.Kind == LiteralKind.Integer)
            {
                if (int.TryParse("-" + literal.Lexeme, NumberStyles.Any, CultureInfo.InvariantCulture, out int parsedInt))
                    return new ScalarValue(ScalarType.Int, new HLSLRegister<object>(parsedInt));
                else if (literal.Lexeme.StartsWith("0x") && int.TryParse("-" + literal.Lexeme.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int parsedHexInt))
                    return new ScalarValue(ScalarType.Int, new HLSLRegister<object>(parsedHexInt));
            }

            var num = EvaluateNumeric(node.Expression);
            switch (node.Operator)
            {
                case OperatorKind.Plus: return num;
                case OperatorKind.Minus: return -num;
                case OperatorKind.Not: return !num;
                case OperatorKind.BitFlip: return ~num;
                case OperatorKind.Increment when node.Expression is NamedExpressionNode named:
                    SetValueSimpleNamed(named.GetName(), num + 1);
                    return num + 1;
                case OperatorKind.Decrement when node.Expression is NamedExpressionNode named:
                    SetValueSimpleNamed(named.GetName(), num - 1);
                    return num - 1;
            }
            throw Error(node, "Invalid prefix unary expression.");
        }
        
        public override HLSLValue VisitPostfixUnaryExpressionNode(PostfixUnaryExpressionNode node)
        {
            var num = EvaluateNumeric(node.Expression);
            switch (node.Operator)
            {
                case OperatorKind.Increment when node.Expression is NamedExpressionNode named:
                    SetValueSimpleNamed(named.GetName(), num + 1);
                    return num;
                case OperatorKind.Decrement when node.Expression is NamedExpressionNode named:
                    SetValueSimpleNamed(named.GetName(), num - 1);
                    return num;
            }
            throw Error(node, "Invalid postfix unary expression.");
        }
        
        public override HLSLValue VisitFieldAccessExpressionNode(FieldAccessExpressionNode node)
        {
            var target = Visit(node.Target);
            string field = node.Name.Identifier;

            // Vector swizzle
            if (target is VectorValue vec)
            {
                if (field.Length > 4)
                    throw Error($"Invalid vector swizzle '{field}'.");
                return vec.Swizzle(field);
            }
            // Scalar swizzle
            else if (target is ScalarValue scalar)
            {
                return scalar.BroadcastToVector(1).Swizzle(field);
            }
            // Matrix swizzle
            else if (target is MatrixValue mat)
            {
                return mat.Swizzle(field);
            }

            var targetStruct = target as StructValue;
            if (targetStruct is null)
                throw Error(node.Target, "Expected a struct or numeric type for field access.");
            return targetStruct.Members[node.Name];
        }

        public override HLSLValue VisitMethodCallExpressionNode(MethodCallExpressionNode node)
        {
            HLSLValue[] args = new HLSLValue[node.Arguments.Count];
            for (int i = 0; i < args.Length; i++)
            {
                args[i] = Visit(node.Arguments[i]);
            }

            var target = Visit(node.Target);
            if (target is StructValue str)
            {
                if (TryFindMethod(str.Name, node.Name.Identifier, out var method))
                {
                    // Handle out/inout parameters
                    for (int i = 0; i < args.Length; i++)
                    {
                        if (method.Parameters[i].Modifiers.Contains(BindingModifier.Inout) ||
                            method.Parameters[i].Modifiers.Contains(BindingModifier.Out))
                        {
                            if (TryGetLValueReference(node.Arguments[i], out var lvalRef))
                                args[i] = lvalRef;
                        }
                    }

                    return InvokeMethod(str, method, args);
                }

                throw Error(node, $"Couldn't find method '{node.Name.Identifier}' on type '{str.Name}'.");
            }

            if (target is ResourceValue resource)
            {
                if (HLSLIntrinsics.TryInvokeResourceMethod(executionState, resource, node.Name.Identifier, args, out HLSLValue result))
                    return result;
                else
                    throw Error(node, $"Can't call unknown method '{resource}.{node.Name.Identifier}'.");
            }

            if (target is PredefinedObjectValue pre)
            {
                throw Error(node, $"Can't call method '{node.Name.Identifier}' on '{pre}.{node.Name.Identifier}', as the resource hasn't been initialized.");
            }

            throw Error(node, $"Can't call method '{node.Name.Identifier}' on non-struct type.");
        }
        
        public override HLSLValue VisitFunctionCallExpressionNode(FunctionCallExpressionNode node)
        {
            HLSLValue[] args = new HLSLValue[node.Arguments.Count];
            for (int i = 0; i < args.Length; i++)
            {
                args[i] = Visit(node.Arguments[i]);
            }

            string name = node.Name.GetName();
            if (callbacks.ContainsKey(name))
                return callbacks[name](executionState, node.Arguments.ToArray());
            
            // Handle out/inout parameters
            FunctionDefinitionNode func = context.GetFunction(this, name, args);
            for (int i = 0; i < args.Length; i++)
            {
                bool isInoutIntrinsic = HLSLIntrinsics.IsIntrinsicInoutParameter(name, i);

                bool isInoutUser =
                    func != null &&
                    (func.Parameters[i].Modifiers.Contains(BindingModifier.Inout) ||
                    func.Parameters[i].Modifiers.Contains(BindingModifier.Out));

                if (isInoutIntrinsic || isInoutUser)
                {
                    if (TryGetLValueReference(node.Arguments[i], out var lvalRef))
                        args[i] = lvalRef;
                }
            }
          
            return CallFunction(name, args);
        }

        public override HLSLValue VisitNumericConstructorCallExpressionNode(NumericConstructorCallExpressionNode node)
        {
            var args = new NumericValue[node.Arguments.Count];
            for (int i = 0; i < args.Length; i++)
            {
                args[i] = Visit(node.Arguments[i]) as NumericValue;
                if (args[i] is null)
                    throw Error(node, "Expected numeric arguments as inputs to numeric constructor.");
            }
            return ConstructNumericValue(node.Kind, args);
        }

        public override HLSLValue VisitElementAccessExpressionNode(ElementAccessExpressionNode node)
        {
            HLSLValue arr = Visit(node.Target);
            ScalarValue target = EvaluateScalar(node.Index);
            if (arr is ArrayValue arrValue)
            {
                if (target.Value.IsVarying)
                {
                    HLSLValue[] values = new HLSLValue[executionState.GetThreadCount()];
                    for (int threadIndex = 0; threadIndex < executionState.GetThreadCount(); threadIndex++)
                    {
                        var index = target.Value.Get(threadIndex);
                        values[threadIndex] = HLSLValueUtils.Scalarize(arrValue.Values[Convert.ToInt32(index)], threadIndex);
                    }
                    HLSLValue result = HLSLValueUtils.Vectorize(values[0], executionState.GetThreadCount());
                    for (int threadIndex = 0; threadIndex < executionState.GetThreadCount(); threadIndex++)
                    {
                        result = HLSLValueUtils.SetThreadValue(result, threadIndex, values[threadIndex]);
                    }
                    return result;
                }
                else
                {
                    return arrValue.Values[Convert.ToInt32(target.Value.UniformValue)];
                }
            }
            else if (arr is VectorValue vec)
            {
                if (target.Value.IsVarying)
                {
                    HLSLValue[] values = new HLSLValue[executionState.GetThreadCount()];
                    for (int threadIndex = 0; threadIndex < executionState.GetThreadCount(); threadIndex++)
                    {
                        var index = target.Value.Get(threadIndex);
                        values[threadIndex] = HLSLValueUtils.Scalarize(vec[Convert.ToInt32(index)], threadIndex);
                    }
                    HLSLValue result = HLSLValueUtils.Vectorize(values[0], executionState.GetThreadCount());
                    for (int threadIndex = 0; threadIndex < executionState.GetThreadCount(); threadIndex++)
                    {
                        result = HLSLValueUtils.SetThreadValue(result, threadIndex, values[threadIndex]);
                    }
                    return result;
                }
                else
                {
                    return vec[Convert.ToInt32(target.Value.UniformValue)];
                }
            }
            else if (arr is MatrixValue mat)
            {
                if (target.Value.IsVarying)
                {
                    HLSLValue[] values = new HLSLValue[executionState.GetThreadCount()];
                    for (int threadIndex = 0; threadIndex < executionState.GetThreadCount(); threadIndex++)
                    {
                        var index = target.Value.Get(threadIndex);
                        ScalarValue[] rowVec = new ScalarValue[mat.Columns];
                        for (int i = 0; i < mat.Columns; i++)
                            rowVec[i] = mat[Convert.ToInt32(index), i];
                        values[threadIndex] = HLSLValueUtils.Scalarize(VectorValue.FromScalars(rowVec), threadIndex);
                    }
                    HLSLValue result = HLSLValueUtils.Vectorize(values[0], executionState.GetThreadCount());
                    for (int threadIndex = 0; threadIndex < executionState.GetThreadCount(); threadIndex++)
                    {
                        result = HLSLValueUtils.SetThreadValue(result, threadIndex, values[threadIndex]);
                    }
                    return result;
                }
                else
                {
                    ScalarValue[] rowVec = new ScalarValue[mat.Columns];
                    for (int i = 0; i < mat.Columns; i++)
                        rowVec[i] = mat[Convert.ToInt32(target.Value.UniformValue), i];
                    return VectorValue.FromScalars(rowVec);
                }
            }
            throw Error(node, "Invalid element access.");
        }

        public override HLSLValue VisitCastExpressionNode(CastExpressionNode node)
        {
            var sourceValue = Visit(node.Expression);
            if (sourceValue is ReferenceValue refVal)
                sourceValue = refVal.Get();

            // Resolve any typedef alias on the target type.
            var targetKind = ResolveType(node.Kind);

            // If source or target involves an array type, use flatten-then-pack strategy.
            bool isTargetArray = node.ArrayRanks.Count > 0;
            if (isTargetArray || sourceValue is ArrayValue)
            {
                var flattened = HLSLValueUtils.FlattenToScalars(sourceValue);

                if (isTargetArray)
                {
                    int arrayLen = Convert.ToInt32(EvaluateNumeric(node.ArrayRanks[0].Dimension).GetThreadValue(0));
                    int components = 0;
                    switch (targetKind)
                    {
                        case ScalarTypeNode: components = 1; break;
                        case VectorTypeNode vt: components = vt.Dimension; break;
                        case MatrixTypeNode mt: components = mt.FirstDimension * mt.SecondDimension; break;
                        default: throw Error(node, $"Unsupported element type in array cast.");
                    }
                    var elements = new HLSLValue[arrayLen];
                    for (int i = 0; i < arrayLen; i++)
                    {
                        int offset = (i * components);
                        int end = offset + components;
                        elements[i] = HLSLValueUtils.PackScalarsToNumeric(flattened[offset..end], targetKind);
                    }
                    return new ArrayValue(elements);
                }
                else
                {
                    return HLSLValueUtils.PackScalarsToNumeric(flattened, targetKind);
                }
            }

            // Otherwise, regular numeric cast
            if (sourceValue is not NumericValue numeric)
                throw Error(node, $"Expected a numeric expression, but got a {sourceValue.GetType().Name}.");

            switch (targetKind)
            {
                case ScalarTypeNode scalarType when numeric is ScalarValue scalar:
                    return scalar.Cast(scalarType.Kind);
                case VectorTypeNode vectorType:
                    return numeric.BroadcastToVector(vectorType.Dimension).Cast(vectorType.Kind);
                case MatrixTypeNode matrixType:
                    return numeric.BroadcastToMatrix(matrixType.FirstDimension, matrixType.SecondDimension).Cast(matrixType.Kind);
                case GenericVectorTypeNode genVectorType:
                    int dims = Convert.ToInt32(EvaluateScalar(genVectorType.Dimension).Cast(ScalarType.Int).GetThreadValue(0));
                    return numeric.BroadcastToVector(dims).Cast(genVectorType.Kind);
                case GenericMatrixTypeNode genMatrixType:
                    int rows = Convert.ToInt32(EvaluateScalar(genMatrixType.FirstDimension).Cast(ScalarType.Int).GetThreadValue(0));
                    int cols = Convert.ToInt32(EvaluateScalar(genMatrixType.SecondDimension).Cast(ScalarType.Int).GetThreadValue(0));
                    return numeric.BroadcastToMatrix(rows, cols).Cast(genMatrixType.Kind);
                case UserDefinedNamedTypeNode named when node.Expression is LiteralExpressionNode:
                    var structType = context.GetStruct(named.GetName());
                    return interpreter.CreateStructValueFilledWith(structType, numeric);
                default:
                    throw Error(node, "Invalid cast.");
            }
        }

        public override HLSLValue VisitArrayInitializerExpressionNode(ArrayInitializerExpressionNode node)
        {
            var elems = VisitMany(node.Elements);
            return new ArrayValue(elems.ToArray());
        }

        public override HLSLValue VisitTernaryExpressionNode(TernaryExpressionNode node)
        {
            var cond = EvaluateNumeric(node.Condition);
            var left = EvaluateNumeric(node.TrueCase);
            var right = EvaluateNumeric(node.FalseCase);

            return HLSLIntrinsics.Select(cond, left, right);
        }

        public override HLSLValue VisitSamplerStateLiteralExpressionNode(SamplerStateLiteralExpressionNode node)
        {
            // Legacy sampler_state { ... } syntax is always a non-comparison sampler.
            return HLSLSamplerStateBuilder.Build(false, node.States, this);
        }
    }
}
