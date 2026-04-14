using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityShaderParser.HLSL;

namespace UnityShaderParser.Test
{
    public static class HLSLTypeUtils
    {
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

        public static int GetDeclaratorArrayLength(IEnumerable<ArrayRankNode> arrayRanks)
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
