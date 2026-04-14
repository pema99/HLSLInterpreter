using System;
using System.Collections.Generic;
using System.Linq;
using UnityShaderParser.Common;
using UnityShaderParser.HLSL;

namespace UnityShaderParser.Test
{
    public static class HLSLValueUtils
    {
        public static int VectorSwizzleCharToIndex(char c)
        {
            switch (c)
            {
                case 'x': case 'r': case 's': return 0;
                case 'y': case 'g': case 't': return 1;
                case 'z': case 'b': case 'p': return 2;
                case 'w': case 'a': case 'q': return 3;
                default: throw new InvalidOperationException($"Invalid swizzle character '{c}'");
            }
        }

        public static char IndexToVectorSwizzleChar(int index)
        {
            switch (index)
            {
                case 0: return 'x';
                case 1: return 'y';
                case 2: return 'z';
                case 3: return 'w';
                default: throw new InvalidOperationException($"Invalid component index {index}");
            }
        }

        public static int[] MatrixSwizzleStringToIndices(string swizzle, int columns)
        {
            bool zeroBased = swizzle.Length >= 2 && swizzle[1] == 'm';
            string[] parts = zeroBased
                ? swizzle.Split(new[] { "_m" }, StringSplitOptions.RemoveEmptyEntries)
                : swizzle.Split(new[] { "_"  }, StringSplitOptions.RemoveEmptyEntries);
            int offset = zeroBased ? 0 : 1;
            return parts.Select(p => (p[0] - '0' - offset) * columns + (p[1] - '0' - offset)).ToArray();
        }


        public static object GetZeroValue(ScalarType type)
        {
            switch (type)
            {
                case ScalarType.Void:
                    return null;
                case ScalarType.Bool:
                    return default(bool);
                case ScalarType.Int:
                case ScalarType.Min16Int:
                case ScalarType.Min12Int:
                    return default(int);
                case ScalarType.Uint:
                case ScalarType.Min16Uint:
                case ScalarType.Min12Uint:
                    return default(uint);
                case ScalarType.Half:
                case ScalarType.Float:
                case ScalarType.Min16Float:
                case ScalarType.Min10Float:
                case ScalarType.UNormFloat:
                case ScalarType.SNormFloat:
                    return default(float);
                case ScalarType.Double:
                    return default(double);
                case ScalarType.String:
                    return string.Empty;
                case ScalarType.Char:
                    return default(char);
                default:
                    throw new InvalidOperationException();
            }
        }

        public static object GetOneValue(ScalarType type)
        {
            switch (type)
            {
                case ScalarType.Void:
                    return null;
                case ScalarType.Bool:
                    return true;
                case ScalarType.Int:
                case ScalarType.Min16Int:
                case ScalarType.Min12Int:
                    return 1;
                case ScalarType.Uint:
                case ScalarType.Min16Uint:
                case ScalarType.Min12Uint:
                    return 1u;
                case ScalarType.Half:
                case ScalarType.Float:
                case ScalarType.Min16Float:
                case ScalarType.Min10Float:
                case ScalarType.UNormFloat:
                case ScalarType.SNormFloat:
                    return 1.0f;
                case ScalarType.Double:
                    return 1.0;
                case ScalarType.String:
                    return string.Empty;
                case ScalarType.Char:
                    return (char)1;
                default:
                    throw new InvalidOperationException();
            }
        }

        public static NumericValue GetZeroValue(NumericValue val)
        {
            return val.Map(x =>
            {
                return GetZeroValue(val.Type);
            });
        }

        public static NumericValue GetOneValue(NumericValue val)
        {
            return val.Map(x =>
            {
                return GetOneValue(val.Type);
            });
        }

        public static bool IsFloat(ScalarType type)
        {
            switch (type)
            {
                case ScalarType.Float:
                case ScalarType.Double:
                case ScalarType.Min16Float:
                case ScalarType.Min10Float:
                case ScalarType.UNormFloat:
                case ScalarType.SNormFloat:
                    return true;
                default:
                    return false;
            }
        }

        public static bool IsUint(ScalarType type)
        {
            switch (type)
            {
                case ScalarType.Uint:
                case ScalarType.Min16Uint:
                case ScalarType.Min12Uint:
                    return true;
                default:
                    return false;
            }
        }

        public static bool IsInt(ScalarType type)
        {
            switch (type)
            {
                case ScalarType.Int:
                case ScalarType.Min16Int:
                case ScalarType.Min12Int:
                    return true;
                default:
                    return false;
            }
        }

        public static int GetScalarRank(ScalarType left)
        {
            switch (left)
            {
                case ScalarType.Bool: return 0;
                case ScalarType.Min12Uint: return 1;
                case ScalarType.Min16Uint: return 2;
                case ScalarType.Uint: return 3;
                case ScalarType.Min12Int: return 4;
                case ScalarType.Min16Int: return 5;
                case ScalarType.Int: return 6;
                case ScalarType.Min10Float: return 7;
                case ScalarType.Min16Float: return 8;
                case ScalarType.Half: return 9;
                case ScalarType.SNormFloat: return 10;
                case ScalarType.UNormFloat: return 11;
                case ScalarType.Float: return 12;
                case ScalarType.Double: return 13;
                default:
                    return -1;
            }
        }

        // Given 2 params, pick the one with highest rank.
        public static ScalarType PromoteScalarType(ScalarType left, ScalarType right)
        {
            return GetScalarRank(left) > GetScalarRank(right) ? left : right;
        }

        public static ScalarType PromoteForBitwiseBinOp(ScalarType left, ScalarType right)
        {
            if (IsUint(left) && IsUint(right))
                return PromoteScalarType(left, right);
            if (IsInt(left) && IsInt(right))
                return PromoteScalarType(left, right);
            if (IsUint(left))
                return left;
            if (IsUint(right))
                return right;
            if (IsInt(left))
                return left;
            if (IsInt(right))
                return right;
            return ScalarType.Uint;
        }

        public static object CastNumeric(ScalarType type, object value)
        {
            switch (type)
            {
                case ScalarType.Void:
                    return null;
                case ScalarType.Bool:
                    return Convert.ToBoolean(value);
                case ScalarType.Int:
                case ScalarType.Min16Int:
                case ScalarType.Min12Int:
                    if (value is float fi) return (int)fi;
                    else if (value is double di) return (int)di;
                    else if (value is uint ui) return (int)ui;
                    else return Convert.ToInt32(value);
                case ScalarType.Uint:
                case ScalarType.Min16Uint:
                case ScalarType.Min12Uint:
                    if (value is float fu) return (uint)fu;
                    else if (value is double du) return (uint)du;
                    else if (value is int iu) return (uint)iu;
                    else return Convert.ToUInt32(value);
                case ScalarType.Double:
                    return Convert.ToDouble(value);
                case ScalarType.Half:
                case ScalarType.Float:
                case ScalarType.Min16Float:
                case ScalarType.Min10Float:
                case ScalarType.UNormFloat:
                case ScalarType.SNormFloat:
                    return Convert.ToSingle(value);
                case ScalarType.String:
                    return Convert.ToString(value);
                case ScalarType.Char:
                    return Convert.ToChar(value);
                default:
                    throw new InvalidOperationException();
            }
        }

        public static (NumericValue newLeft, NumericValue newRight) PromoteThreadCount(NumericValue left, NumericValue right)
        {
            int leftThreadCount = left.ThreadCount;
            int rightThreadCount = right.ThreadCount;
            if (leftThreadCount < rightThreadCount)
                left = left.Vectorize(rightThreadCount);
            else if (rightThreadCount < leftThreadCount)
                right = right.Vectorize(leftThreadCount);

            return (left, right);
        }

        public static (NumericValue newLeft, NumericValue newRight) PromoteShape(NumericValue left, NumericValue right)
        {
            bool needMatrix = left is MatrixValue || right is MatrixValue;
            bool needVector = left is VectorValue || right is VectorValue;

            if (needMatrix)
            {
                (int leftRows, int leftColumns) = left.TensorSize;
                (int rightRows, int rightColumns) = right.TensorSize;
                int newRows = Math.Max(leftRows, rightRows);
                int newColumns = Math.Max(leftColumns, rightColumns);
                if (leftRows != newRows || leftColumns != newColumns)
                    left = left.BroadcastToMatrix(newRows, newColumns);
                if (rightRows != newRows || rightColumns != newColumns)
                    right = right.BroadcastToMatrix(newRows, newColumns);
            }

            else if (needVector)
            {
                (int leftSize, _) = left.TensorSize;
                (int rightSize, _) = right.TensorSize;
                int newSize = Math.Max(leftSize, rightSize);
                // Also broadcast a ScalarValue to VectorValue even when sizes are equal,
                // so Map2 always receives two operands of the same concrete type.
                if (leftSize != newSize || (left is ScalarValue && right is VectorValue))
                    left = left.BroadcastToVector(newSize);
                if (rightSize != newSize || (right is ScalarValue && left is VectorValue))
                    right = right.BroadcastToVector(newSize);
            }

            return (left, right);
        }

        public static (NumericValue newLeft, NumericValue newRight) PromoteType(NumericValue left, NumericValue right, bool bitwiseOp)
        {
            ScalarType type = bitwiseOp
                ? PromoteForBitwiseBinOp(left.Type, right.Type)
                : PromoteScalarType(left.Type, right.Type);

            return (left.Cast(type), right.Cast(type));
        }

        // Given 2 params, promote such that no information is lost.
        // This includes promoting SGPR to VGPR.
        public static (NumericValue newLeft, NumericValue newRight) Promote(NumericValue left, NumericValue right, bool bitwiseOp)
        {
            (left, right) = PromoteThreadCount(left, right);

            // Fast path
            if (left.Type == right.Type && left.GetType() == right.GetType())
                return (left, right);

            (left, right) = PromoteShape(left, right);

            return PromoteType(left, right, bitwiseOp);
        }

        // Cast "right" to match the type of "left" and return it.
        // Performs any implicit conversions, either promotion or demotion, needed for an assignment,
        public static HLSLValue CastForAssignment(HLSLValue left, HLSLValue right)
        {
            if (left is NumericValue leftNum && right is NumericValue rightNum)
            {
                int leftThreadCount = leftNum.ThreadCount;
                int rightThreadCount = rightNum.ThreadCount;
                if (leftThreadCount < rightThreadCount)
                    leftNum = leftNum.Vectorize(rightThreadCount);
                else if (rightThreadCount < leftThreadCount)
                    rightNum = rightNum.Vectorize(leftThreadCount);

                ScalarType type = leftNum.Type;

                bool needMatrix = leftNum is MatrixValue;
                bool needVector = leftNum is VectorValue;

                if (needMatrix)
                {
                    (int newRows, int newColumns) = leftNum.TensorSize;
                    var resizedRight = rightNum.BroadcastToMatrix(newRows, newColumns);
                    return resizedRight.Cast(type);
                }

                if (needVector)
                {
                    int newSize = leftNum.TensorSize.rows;
                    var resizedRight = rightNum.BroadcastToVector(newSize);
                    return resizedRight.Cast(type);
                }

                return rightNum.Cast(type);
            }

            return right;
        }

        private static HLSLRegister<T> Map2Registers<T>(HLSLRegister<T> left, HLSLRegister<T> right, Func<T, T, T> mapper)
        {
            if (left.IsVarying && right.IsVarying)
            {
                T[] mapped = new T[left.VaryingValues.Length];
                for (int i = 0; i < mapped.Length; i++)
                    mapped[i] = mapper(left.VaryingValues[i], right.VaryingValues[i]);
                return new HLSLRegister<T>(mapped);
            }
            else if (!left.IsVarying && !right.IsVarying)
            {
                return new HLSLRegister<T>(mapper(left.UniformValue, right.UniformValue));
            }
            else
            {
                throw new InvalidOperationException("Cannot map varying and uniform register together.");
            }
        }

        public static NumericValue Map2(NumericValue left, NumericValue right, Func<object, object, object> mapper)
        {
            if (left.TensorSize != right.TensorSize)
                throw new ArgumentException("Sizes of operands must match.");
            if (left is ScalarValue scalarLeft && right is ScalarValue scalarRight)
                return new ScalarValue(scalarLeft.Type, Map2Registers(scalarLeft.Value, scalarRight.Value, mapper));
            if (left is VectorValue vectorLeft && right is VectorValue vectorRight)
            {
                var mapped = Map2Registers(vectorLeft.Values, vectorRight.Values, (x, y) =>
                {
                    object[] result = new object[vectorLeft.Size];
                    for (int i = 0; i < result.Length; i++)
                        result[i] = mapper(x[i], y[i]);
                    return result;
                });
                return new VectorValue(vectorLeft.Type, mapped);
            }
            if (left is MatrixValue matrixLeft && right is MatrixValue matrixRight)
            {
                var mapped = Map2Registers(matrixLeft.Values, matrixRight.Values, (x, y) =>
                {
                    object[] result = new object[x.Length];
                    for (int i = 0; i < result.Length; i++)
                        result[i] = mapper(x[i], y[i]);
                    return result;
                });
                return new MatrixValue(matrixLeft.Type, matrixLeft.Rows, matrixLeft.Columns, mapped);
            }
            throw new InvalidOperationException();
        }

        public static HLSLValue Scalarize(HLSLValue value, int threadIndex)
        {
            switch (value)
            {
                case NumericValue num:
                    return num.Scalarize(threadIndex);
                case StructValue str:
                    Dictionary<string, HLSLValue> members = new Dictionary<string, HLSLValue>();
                    foreach (var kvp in str.Members)
                        members.Add(kvp.Key, Scalarize(kvp.Value, threadIndex));
                    return new StructValue(str.Name, members);
                case ArrayValue arr:
                    HLSLValue[] vals = new HLSLValue[arr.Values.Length];
                    for (int i = 0; i < vals.Length; i++)
                        vals[i] = Scalarize(arr.Values[i], threadIndex);
                    return new ArrayValue(vals);
                default:
                    throw new InvalidOperationException();
            }
        }

        public static HLSLValue Vectorize(HLSLValue value, int threadCount)
        {
            switch (value)
            {
                case NumericValue num:
                    return num.Vectorize(threadCount);
                case StructValue str:
                    Dictionary<string, HLSLValue> members = new Dictionary<string, HLSLValue>();
                    foreach (var kvp in str.Members)
                        members.Add(kvp.Key, Vectorize(kvp.Value, threadCount));
                    return new StructValue(str.Name, members);
                case ArrayValue arr:
                    HLSLValue[] vals = new HLSLValue[arr.Values.Length];
                    for (int i = 0; i < vals.Length; i++)
                        vals[i] = Vectorize(arr.Values[i], threadCount);
                    return new ArrayValue(vals);
                default:
                    throw new InvalidOperationException();
            }
        }

        public static HLSLValue SetThreadValue(HLSLValue allValue, int threadIndex, HLSLValue threadValue)
        {
            if (allValue is NumericValue numLeft && threadValue is NumericValue numRight)
            {
                (numLeft, numRight) = Promote(numLeft, numRight, false);
                return numLeft.SetThreadValue(threadIndex, numRight.GetThreadValue(threadIndex));
            }

            if (allValue is StructValue strLeft && threadValue is StructValue strRight)
            {
                Dictionary<string, HLSLValue> members = new Dictionary<string, HLSLValue>();
                foreach (var kvp in strLeft.Members)
                {
                    if (strRight.Members.TryGetValue(kvp.Key, out var rightV))
                        members.Add(kvp.Key, SetThreadValue(kvp.Value, threadIndex, rightV));
                }
                return new StructValue(strLeft.Name, members);
            }

            if (allValue is ArrayValue arrLeft && threadValue is ArrayValue arrRight)
            {
                HLSLValue[] vals = new HLSLValue[arrLeft.Values.Length];
                for (int i = 0; i < vals.Length; i++)
                    vals[i] = SetThreadValue(arrLeft.Values[i], threadIndex, arrRight.Values[i]);
                return new ArrayValue(vals);
            }

            throw new InvalidOperationException();
        }

        public static HLSLRegister<object> MakeScalarSGPR<T>(T val)
        {
            return new HLSLRegister<object>(val);
        }

        public static HLSLRegister<object> MakeScalarVGPR<T>(IEnumerable<T> val)
        {
            return new HLSLRegister<object>(val.Select(x => (object)x).ToArray());
        }

        public static HLSLRegister<object[]> MakeVectorSGPR<T>(IEnumerable<T> val)
        {
            return new HLSLRegister<object[]>(val.Select(x => (object)x).ToArray());
        }

        public static HLSLRegister<object[]> MakeVectorVGPR<T>(IEnumerable<IEnumerable<T>> val)
        {
            return new HLSLRegister<object[]>(val.Select(x => x.Select(y => (object)y).ToArray()).ToArray());
        }

        public static HLSLRegister<object[]> RegisterFromScalars(ScalarValue[] scalars)
        {
            ScalarType type = scalars[0].Type;
            foreach (var scalar in scalars)
            {
                type = HLSLValueUtils.PromoteScalarType(type, scalar.Type);
            }

            int maxThreadCount = scalars.Max(x => x.ThreadCount);
            object[][] result = new object[maxThreadCount][];
            for (int threadIndex = 0; threadIndex < maxThreadCount; threadIndex++)
            {
                result[threadIndex] = new object[scalars.Length];
                for (int channel = 0; channel < scalars.Length; channel++)
                {
                    var scalar = scalars[channel];
                    if (scalar.Type != type)
                        scalar = (ScalarValue)scalar.Cast(type);
                    result[threadIndex][channel] = scalar.GetThreadValue(threadIndex);
                }
            }

            if (maxThreadCount == 1)
                return MakeVectorSGPR(result[0]);
            else
                return MakeVectorVGPR(result);
        }

        public static ScalarValue[] RegisterToScalars(ScalarType type, HLSLRegister<object[]> scalars)
        {
            ScalarValue[] scalarValues = new ScalarValue[scalars.Get(0).Length];
            if (scalars.IsUniform)
            {
                for (int i = 0; i < scalarValues.Length; i++)
                {
                    scalarValues[i] = new ScalarValue(type, HLSLValueUtils.MakeScalarSGPR(scalars.UniformValue[i]));
                }
            }
            else
            {
                for (int i = 0; i < scalarValues.Length; i++)
                {
                    object[] perThreadValues = new object[scalars.Size];
                    for (int threadIndex = 0; threadIndex < scalars.Size; threadIndex++)
                    {
                        perThreadValues[threadIndex] = scalars.VaryingValues[threadIndex][i];
                    }
                    scalarValues[i] = new ScalarValue(type, HLSLValueUtils.MakeScalarVGPR(perThreadValues));
                }
            }
            return scalarValues;
        }

        // Cast "value" to match the given type and return it.
        // Performs any implicit conversions, either promotion or demotion, needed to pass as a function parameter.
        public static HLSLValue CastForParameter(HLSLExpressionEvaluator evaluator, HLSLValue value, TypeNode typeNode)
        {
            typeNode = evaluator.ResolveType(typeNode);
            if (value is NumericValue valueNum && typeNode is NumericTypeNode typeNum)
            {
                HLSLValue reshaped = valueNum;
                if (typeNode is MatrixTypeNode mat)
                    valueNum = valueNum.BroadcastToMatrix(mat.FirstDimension, mat.SecondDimension);

                if (typeNode is VectorTypeNode vec)
                    valueNum = valueNum.BroadcastToVector(vec.Dimension);

                if (typeNode is GenericMatrixTypeNode matGen)
                {
                    int rows = Convert.ToInt32(((ScalarValue)evaluator.Visit(matGen.FirstDimension)).Cast(ScalarType.Int).GetThreadValue(0));
                    int cols = Convert.ToInt32(((ScalarValue)evaluator.Visit(matGen.SecondDimension)).Cast(ScalarType.Int).GetThreadValue(0));
                    valueNum = valueNum.BroadcastToMatrix(rows, cols);
                }

                if (typeNode is GenericVectorTypeNode vecGen)
                    valueNum = valueNum.BroadcastToVector(Convert.ToInt32(((ScalarValue)evaluator.Visit(vecGen.Dimension)).Cast(ScalarType.Int).GetThreadValue(0)));

                return valueNum.Cast(typeNum.Kind);
            }
            return value;
        }

        public static bool TypeEquals(HLSLExpressionEvaluator evaluator, HLSLValue from, TypeNode to, IList<ArrayRankNode> arrayRanks = null)
        {
            if (to is ScalarTypeNode scalarType &&
                from is ScalarValue scalarValue &&
                scalarType.Kind == scalarValue.Type)
                return true;
            if (to is VectorTypeNode vecType &&
                from is VectorValue vecValue &&
                vecType.Kind == vecValue.Type &&
                vecType.Dimension == vecValue.Size)
                return true;
            if (to is GenericVectorTypeNode genVecType &&
                from is VectorValue genVecValue &&
                genVecType.Kind == genVecValue.Type &&
                Convert.ToInt32(((ScalarValue)evaluator.Visit(genVecType.Dimension)).Cast(ScalarType.Int).GetThreadValue(0)) == genVecValue.Size)
                return true;
            if (to is MatrixTypeNode matType
                && from is MatrixValue matValue
                && matType.Kind == matValue.Type
                && matType.FirstDimension == matValue.Rows
                && matType.SecondDimension == matValue.Columns)
                return true;
            if (to is GenericMatrixTypeNode genMatType
                && from is MatrixValue genMatValue
                && genMatType.Kind == genMatValue.Type
                && Convert.ToInt32(((ScalarValue)evaluator.Visit(genMatType.FirstDimension)).Cast(ScalarType.Int).GetThreadValue(0)) == genMatValue.Rows
                && Convert.ToInt32(((ScalarValue)evaluator.Visit(genMatType.SecondDimension)).Cast(ScalarType.Int).GetThreadValue(0)) == genMatValue.Columns)
                return true;
            if (to is StructTypeNode strType &&
                from is StructValue strValue &&
                strType.Name.GetName() == strValue.Name)
                return true;
            if (to is NamedTypeNode namedType &&
                from is StructValue namedStrValue &&
                namedType.GetName()== namedStrValue.Name)
                return true;
            if (to is QualifiedNamedTypeNode qualNamedType &&
                from is StructValue qualNamedStrValue &&
               qualNamedType.GetName() == qualNamedStrValue.Name)
                return true;
            if (to is PredefinedObjectTypeNode preType &&
                from is PredefinedObjectValue preValue &&
                preType.Kind == preValue.Type &&
                preType.TemplateArguments?.Count == preValue.TemplateArguments?.Length)
            {
                for (int i = 0; i < preType.TemplateArguments?.Count; i++)
                {
                    if (preValue.TemplateArguments[i].GetPrettyPrintedCode() != preType.TemplateArguments[i].GetPrettyPrintedCode())
                        return false;
                }
                return true;
            }
            if (arrayRanks != null &&
                from is ArrayValue arrValue &&
                arrValue.Values.Length > 0 &&
                arrayRanks.Count > 0 &&
                arrayRanks[0].Dimension is LiteralExpressionNode litDim &&
                int.Parse(litDim.Lexeme) == arrValue.Values.Length)
            {
                return TypeEquals(evaluator, arrValue.Values[0], to, arrayRanks.Skip(1).ToList());
            }
            if (from is ReferenceValue reference)
            {
                return TypeEquals(evaluator, reference.Get(), to);
            }

            return false;
        }

        // Can we convert a value to a type without loss of information?
        public static bool CanPromoteTo(HLSLExpressionEvaluator evaluator, HLSLValue from, TypeNode to)
        {
            if (from is NumericValue fromNum && to is NumericTypeNode toNum)
            {
                // Casting into a less informative type is not promotion
                if (GetScalarRank(fromNum.Type) > GetScalarRank(toNum.Kind))
                    return false;
                // Scalars broadcast
                if (fromNum is ScalarValue)
                    return true;
                // Vector extension
                if (fromNum is VectorValue fromVec && toNum is VectorTypeNode toVec)
                    return fromVec.Size <= toVec.Dimension;
                // Matrix extension
                if (fromNum is MatrixValue fromMat && toNum is MatrixTypeNode toMat)
                    return fromMat.Rows <= toMat.FirstDimension && fromMat.Columns <= toMat.SecondDimension;
                // Same but for generic
                if (fromNum is VectorValue fromVecGen && toNum is GenericVectorTypeNode toVecGen)
                    return fromVecGen.Size <= Convert.ToInt32(((ScalarValue)evaluator.Visit(toVecGen.Dimension)).Cast(ScalarType.Int).GetThreadValue(0));
                if (fromNum is MatrixValue fromMatGen && toNum is GenericMatrixTypeNode toMatGen)
                {
                    int rows = Convert.ToInt32(((ScalarValue)evaluator.Visit(toMatGen.FirstDimension)).Cast(ScalarType.Int).GetThreadValue(0));
                    int cols = Convert.ToInt32(((ScalarValue)evaluator.Visit(toMatGen.SecondDimension)).Cast(ScalarType.Int).GetThreadValue(0));
                    return fromMatGen.Rows <= rows && fromMatGen.Columns <= cols;
                }
            }
            return false;
        }

        // Can we convert a value to a type with loss of information?
        public static bool CanDemoteTo(HLSLExpressionEvaluator evaluator, HLSLValue from, TypeNode to)
        {
            if (from is NumericValue fromNum && to is NumericTypeNode toNum)
            {
                // Anything can be demoted to an scalar
                if (toNum is ScalarTypeNode)
                    return true;
                // If both are the same type, compare sizes - the from type must be strictly larger
                if (fromNum is VectorValue fromVec && toNum is VectorTypeNode toVec)
                    return fromVec.Size > toVec.Dimension;
                if (fromNum is MatrixValue fromMat && toNum is MatrixTypeNode toMat)
                    return fromMat.Rows > toMat.FirstDimension && fromMat.Columns > toMat.SecondDimension;
                if (fromNum is VectorValue fromVecGen && toNum is GenericVectorTypeNode toVecGen)
                    return fromVecGen.Size > Convert.ToInt32(((ScalarValue)evaluator.Visit(toVecGen.Dimension)).Cast(ScalarType.Int).GetThreadValue(0));
                if (fromNum is MatrixValue fromMatGen && toNum is GenericMatrixTypeNode toMatGen)
                {
                    int rows = Convert.ToInt32(((ScalarValue)evaluator.Visit(toMatGen.FirstDimension)).Cast(ScalarType.Int).GetThreadValue(0));
                    int cols = Convert.ToInt32(((ScalarValue)evaluator.Visit(toMatGen.SecondDimension)).Cast(ScalarType.Int).GetThreadValue(0));
                    return fromMatGen.Rows > rows && fromMatGen.Columns > cols;
                }
            }
            return false;
        }

        // Given a function and a list of parameters, evaluate how well the function matches the parameters
        public static int GetOverloadScore(HLSLExpressionEvaluator evaluator, FunctionDefinitionNode candidate, IList<HLSLValue> parameters)
        {
            if (parameters.Count != candidate.Parameters.Count)
                return -1;

            int score = 0;
            for (int i = 0; i < parameters.Count; i++)
            {
                var from = parameters[i];
                var to = evaluator.ResolveType(candidate.Parameters[i].ParamType);
                if (TypeEquals(evaluator, from, to, candidate.Parameters[i].Declarator.ArrayRanks))
                    score += 3; // Exact match, best case
                else if (CanPromoteTo(evaluator, from, to))
                    score += 2; // Promotion is almost as good
                else if (CanDemoteTo(evaluator, from, to))
                    score += 1; // Demotion is a last resort
            }
            return score;
        }

        // Pick a function overload from a list of candidates based on the parameters. Returns null if no viable overload.
        public static FunctionDefinitionNode PickOverload(HLSLExpressionEvaluator evaluator, IEnumerable<FunctionDefinitionNode> candidates, IList<HLSLValue> parameters)
        {
            int bestScore = -1;
            FunctionDefinitionNode selected = null;
            foreach (var candidate in candidates)
            {
                int score = GetOverloadScore(evaluator, candidate, parameters);
                if (score > bestScore)
                {
                    bestScore = score;
                    selected = candidate;
                }
            }
            return selected;
        }

        public static HLSLValue MergeThreadValues(HLSLValue[] threadValues)
        {
            if (threadValues.Length == 1)
                return threadValues[0];

            HLSLValue result = Vectorize(threadValues[0], threadValues.Length);
            for (int threadIndex = 1; threadIndex < threadValues.Length; threadIndex++)
            {
                result = SetThreadValue(result, threadIndex, threadValues[threadIndex]);
            }
            return result;
        }

        private static int GetDeclaratorArrayLength(IEnumerable<ArrayRankNode> arrayRanks)
        {
            int length = 1;
            foreach (var rank in arrayRanks)
            {
                if (rank.Dimension == null)
                    continue;
                length *= Convert.ToInt32(((ScalarValue)HLSLExpressionEvaluator.EvaluateConstExpr(rank.Dimension)).Value.Get(0));
            }
            return length;
        }

        public static int GetTypeSizeDwords(HLSLInterpreterContext ctx, TypeNode type, IEnumerable<ArrayRankNode> arrayRanks = null)
        {
            type = ctx.ResolveType(type);
            int elementSize;
            switch (type)
            {
                case ScalarTypeNode st:
                    elementSize = st.Kind == ScalarType.Void || st.Kind == ScalarType.String || st.Kind == ScalarType.Char ? 0 : 1;
                    break;
                case VectorTypeNode vt:
                    elementSize = vt.Dimension;
                    break;
                case MatrixTypeNode mt:
                    elementSize = mt.FirstDimension * mt.SecondDimension;
                    break;
                case StructTypeNode str:
                    elementSize = GetStructFields(str, ctx).Sum(f => GetTypeSizeDwords(ctx, f.Kind, f.Decl.ArrayRanks));
                    break;
                case NamedTypeNode namedType when ctx.GetStruct(namedType.GetName()) is { } s:
                    elementSize = GetTypeSizeDwords(ctx, s);
                    break;
                case QualifiedNamedTypeNode qualType when ctx.GetStruct(qualType.GetName()) is { } s:
                    elementSize = GetTypeSizeDwords(ctx, s);
                    break;
                default:
                    elementSize = 0;
                    break;
            }
            return elementSize * (arrayRanks != null ? GetDeclaratorArrayLength(arrayRanks) : 1);
        }

        public static ScalarValue[] FlattenToScalars(HLSLValue value)
        {
            if (value is NumericValue num)
                return num.ToScalars();
            if (value is ArrayValue arr)
                return arr.Values.SelectMany(x => FlattenToScalars(x)).ToArray();
            if (value is StructValue sv)
                return sv.Members.Values.SelectMany(x => FlattenToScalars(x)).ToArray();
            throw new InvalidOperationException();
        }

        public static HLSLValue PackScalars(HLSLInterpreterContext ctx, ScalarValue[] scalars, TypeNode targetType, IEnumerable<ArrayRankNode> arrayRanks = null)
        {
            targetType = ctx.ResolveType(targetType);
            int arrayLen = arrayRanks != null ? GetDeclaratorArrayLength(arrayRanks) : 1;
            if (arrayLen > 1)
            {
                int elementSize = scalars.Length / arrayLen;
                var elements = new HLSLValue[arrayLen];
                for (int i = 0; i < arrayLen; i++)
                {
                    elements[i] = PackScalars(ctx, scalars[(i * elementSize)..((i + 1) * elementSize)], targetType);
                }
                return new ArrayValue(elements);
            }
            switch (targetType)
            {
                case ScalarTypeNode st:
                    return scalars[0].Cast(st.Kind);
                case VectorTypeNode vt:
                    return VectorValue.FromScalars(scalars[..vt.Dimension].Select(s => (ScalarValue)s.Cast(vt.Kind)).ToArray());
                case MatrixTypeNode mt:
                    int components = mt.FirstDimension * mt.SecondDimension;
                    return MatrixValue.FromScalars(mt.FirstDimension, mt.SecondDimension, scalars[..components].Select(s => (ScalarValue)s.Cast(mt.Kind)).ToArray());
                case StructTypeNode st:
                {
                    var members = new Dictionary<string, HLSLValue>();
                    int offset = 0;
                    foreach (var (kind, decl) in GetStructFields(st, ctx))
                    {
                        int fieldSize = GetTypeSizeDwords(ctx, kind, decl.ArrayRanks);
                        members[decl.Name] = PackScalars(ctx, scalars[offset..(offset + fieldSize)], kind, decl.ArrayRanks);
                        offset += fieldSize;
                    }
                    return new StructValue(ctx.GetQualifiedName(st.Name.GetName()), members);
                }
                case NamedTypeNode namedType:
                    return PackScalars(ctx, scalars, ctx.GetStruct(namedType.GetName()));
                case QualifiedNamedTypeNode qualType:
                    return PackScalars(ctx, scalars, ctx.GetStruct(qualType.GetName()));
                default:
                    throw new NotImplementedException();
            }
        }

        public static IEnumerable<(TypeNode Kind, VariableDeclaratorNode Decl)> GetStructFields(StructTypeNode structType, HLSLInterpreterContext ctx)
        {
            foreach (var baseTypeName in structType.Inherits)
            {
                var baseStruct = ctx.GetStruct(baseTypeName.GetName());
                if (baseStruct != null)
                {
                    foreach (var kv in GetStructFields(baseStruct, ctx))
                    {
                        yield return kv;
                    }
                }
            }
            foreach (var field in structType.Fields)
            {
                foreach (var decl in field.Declarators)
                {
                    yield return (field.Kind, decl);
                }
            }
        }
    }
}
