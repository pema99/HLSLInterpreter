using System;
using UnityShaderParser.HLSL;

namespace HLSL
{
    public static class HLSLOperators
    {
        #region Helpers
        public static NumericValue BinOp(NumericValue left, NumericValue right, ScalarType type, Func<ScalarType, RawValue, RawValue, RawValue> mapper)
        {
            static HLSLRegister<RawValue[]> Map2VectorRegisters(HLSLRegister<RawValue[]> left, HLSLRegister<RawValue[]> right, ScalarType type, Func<ScalarType, RawValue, RawValue, RawValue> mapper)
            {
                if (left.IsVarying && right.IsVarying)
                {
                    var lv = left.VaryingValues;
                    var rv = right.VaryingValues;
                    RawValue[][] mapped = new RawValue[lv.Length][];
                    for (int t = 0; t < mapped.Length; t++)
                    {
                        var lt = lv[t];
                        var rt = rv[t];
                        RawValue[] row = new RawValue[lt.Length];
                        for (int i = 0; i < row.Length; i++)
                        {
                            row[i] = mapper(type, lt[i], rt[i]);
                        }
                        mapped[t] = row;
                    }
                    return new HLSLRegister<RawValue[]>(mapped);
                }
                if (!left.IsVarying && !right.IsVarying)
                {
                    var lu = left.UniformValue;
                    var ru = right.UniformValue;
                    RawValue[] row = new RawValue[lu.Length];
                    for (int i = 0; i < row.Length; i++)
                    {
                        row[i] = mapper(type, lu[i], ru[i]);
                    }
                    return new HLSLRegister<RawValue[]>(row);
                }
                throw new InvalidOperationException("Cannot map varying and uniform register together.");
            }

            static HLSLRegister<RawValue> Map2ScalarRegisters(HLSLRegister<RawValue> left, HLSLRegister<RawValue> right, ScalarType type, Func<ScalarType, RawValue, RawValue, RawValue> mapper)
            {
                if (left.IsVarying && right.IsVarying)
                {
                    RawValue[] mapped = new RawValue[left.VaryingValues.Length];
                    for (int i = 0; i < mapped.Length; i++)
                    {
                        mapped[i] = mapper(type, left.VaryingValues[i], right.VaryingValues[i]);
                    }
                    return new HLSLRegister<RawValue>(mapped);
                }
                else if (!left.IsVarying && !right.IsVarying)
                {
                    return new HLSLRegister<RawValue>(mapper(type, left.UniformValue, right.UniformValue));
                }
                else
                {
                    throw new InvalidOperationException("Cannot map varying and uniform register together.");
                }
            }

            if (left.TensorSize != right.TensorSize)
                throw new ArgumentException("Sizes of operands must match.");
            if (left is ScalarValue scalarLeft && right is ScalarValue scalarRight)
                return new ScalarValue(type, Map2ScalarRegisters(scalarLeft.Value, scalarRight.Value, type, mapper));
            if (left is VectorValue vectorLeft && right is VectorValue vectorRight)
                return new VectorValue(type, Map2VectorRegisters(vectorLeft.Values, vectorRight.Values, type, mapper));
            if (left is MatrixValue matrixLeft && right is MatrixValue matrixRight)
                return new MatrixValue(type, matrixLeft.Rows, matrixLeft.Columns, Map2VectorRegisters(matrixLeft.Values, matrixRight.Values, type, mapper));
            throw new InvalidOperationException();
        }

        public static RawValue Add(ScalarType type, RawValue left, RawValue right)
        {
            if (type == ScalarType.Float) return left.Float + right.Float;
            if (HLSLTypeUtils.IsInt(type)) return left.Int + right.Int;
            if (HLSLTypeUtils.IsUint(type)) return left.Uint + right.Uint;
            if (type == ScalarType.Double) return left.Double + right.Double;
            if (HLSLTypeUtils.IsFloat(type)) return left.Float + right.Float;
            if (type == ScalarType.Bool) return ((left.Bool ? 1 : 0) + (right.Bool ? 1 : 0)) != 0;
            throw new InvalidOperationException();
        }

        public static RawValue Mul(ScalarType type, RawValue left, RawValue right)
        {
            if (type == ScalarType.Float) return left.Float * right.Float;
            if (HLSLTypeUtils.IsInt(type)) return left.Int * right.Int;
            if (HLSLTypeUtils.IsUint(type)) return left.Uint * right.Uint;
            if (type == ScalarType.Double) return left.Double * right.Double;
            if (HLSLTypeUtils.IsFloat(type)) return left.Float * right.Float;
            if (type == ScalarType.Bool) return ((left.Bool ? 1 : 0) * (right.Bool ? 1 : 0)) != 0;
            throw new InvalidOperationException();
        }

        private static RawValue Sub(ScalarType type, RawValue left, RawValue right)
        {
            if (type == ScalarType.Float) return left.Float - right.Float;
            if (HLSLTypeUtils.IsInt(type)) return left.Int - right.Int;
            if (HLSLTypeUtils.IsUint(type)) return left.Uint - right.Uint;
            if (type == ScalarType.Double) return left.Double - right.Double;
            if (HLSLTypeUtils.IsFloat(type)) return left.Float - right.Float;
            if (type == ScalarType.Bool) return ((left.Bool ? 1 : 0) - (right.Bool ? 1 : 0)) != 0;
            throw new InvalidOperationException();
        }

        private static RawValue Div(ScalarType type, RawValue left, RawValue right)
        {
            if (type == ScalarType.Float) return left.Float / right.Float;
            if (HLSLTypeUtils.IsInt(type)) return left.Int / right.Int;
            if (HLSLTypeUtils.IsUint(type)) return left.Uint / right.Uint;
            if (type == ScalarType.Double) return left.Double / right.Double;
            if (HLSLTypeUtils.IsFloat(type)) return left.Float / right.Float;
            if (type == ScalarType.Bool) return ((left.Bool ? 1 : 0) / (right.Bool ? 1 : 0)) != 0;
            throw new InvalidOperationException();
        }

        private static RawValue Mod(ScalarType type, RawValue left, RawValue right)
        {
            if (type == ScalarType.Float) return left.Float % right.Float;
            if (HLSLTypeUtils.IsInt(type)) return left.Int % right.Int;
            if (HLSLTypeUtils.IsUint(type)) return left.Uint % right.Uint;
            if (type == ScalarType.Double) return left.Double % right.Double;
            if (HLSLTypeUtils.IsFloat(type)) return left.Float % right.Float;
            if (type == ScalarType.Bool) return ((left.Bool ? 1 : 0) % (right.Bool ? 1 : 0)) != 0;
            throw new InvalidOperationException();
        }

        private static RawValue BitSHL(ScalarType type, RawValue left, RawValue right)
        {
            if (HLSLTypeUtils.IsInt(type)) return left.Int << right.Int;
            if (HLSLTypeUtils.IsUint(type)) return left.Uint << (int)right.Uint;
            throw new InvalidOperationException();
        }

        private static RawValue BitSHR(ScalarType type, RawValue left, RawValue right)
        {
            if (HLSLTypeUtils.IsInt(type)) return left.Int >> right.Int;
            if (HLSLTypeUtils.IsUint(type)) return left.Uint >> (int)right.Uint;
            throw new InvalidOperationException();
        }

        private static RawValue BitAnd(ScalarType type, RawValue left, RawValue right)
        {
            if (HLSLTypeUtils.IsInt(type)) return left.Int & right.Int;
            if (HLSLTypeUtils.IsUint(type)) return left.Uint & right.Uint;
            throw new InvalidOperationException();
        }

        private static RawValue BitOr(ScalarType type, RawValue left, RawValue right)
        {
            if (HLSLTypeUtils.IsInt(type)) return left.Int | right.Int;
            if (HLSLTypeUtils.IsUint(type)) return left.Uint | right.Uint;
            throw new InvalidOperationException();
        }

        private static RawValue BitXor(ScalarType type, RawValue left, RawValue right)
        {
            if (HLSLTypeUtils.IsInt(type)) return left.Int ^ right.Int;
            if (HLSLTypeUtils.IsUint(type)) return left.Uint ^ right.Uint;
            throw new InvalidOperationException();
        }

        private static RawValue BoolAnd(ScalarType type, RawValue left, RawValue right)
        {
            return left.Bool && right.Bool;
        }

        private static RawValue BoolOr(ScalarType type, RawValue left, RawValue right)
        {
            return left.Bool || right.Bool;
        }

        private static RawValue BitNot(ScalarType type, RawValue left)
        {
            if (HLSLTypeUtils.IsInt(type)) return ~left.Int;
            if (HLSLTypeUtils.IsUint(type)) return ~left.Uint;
            throw new InvalidOperationException();
        }

        private static RawValue Negate(ScalarType type, RawValue left)
        {
            if (type == ScalarType.Float) return -left.Float;
            if (HLSLTypeUtils.IsInt(type)) return -left.Int;
            if (HLSLTypeUtils.IsUint(type)) return (int)-left.Uint;
            if (type == ScalarType.Double) return -left.Double;
            if (HLSLTypeUtils.IsFloat(type)) return -left.Float;
            if (type == ScalarType.Bool) return (-(left.Bool ? 1 : 0)) != 0;
            throw new InvalidOperationException();
        }

        private static RawValue BoolNegate(RawValue left)
        {
            return !left.Bool;
        }

        private static RawValue Less(ScalarType type, RawValue left, RawValue right)
        {
            if (type == ScalarType.Float) return left.Float < right.Float;
            if (HLSLTypeUtils.IsInt(type)) return left.Int < right.Int;
            if (HLSLTypeUtils.IsUint(type)) return left.Uint < right.Uint;
            if (type == ScalarType.Double) return left.Double < right.Double;
            if (HLSLTypeUtils.IsFloat(type)) return left.Float < right.Float;
            if (type == ScalarType.Bool) return (left.Bool ? 1 : 0) < (right.Bool ? 1 : 0);
            throw new InvalidOperationException();
        }

        private static RawValue Greater(ScalarType type, RawValue left, RawValue right)
        {
            if (type == ScalarType.Float) return left.Float > right.Float;
            if (HLSLTypeUtils.IsInt(type)) return left.Int > right.Int;
            if (HLSLTypeUtils.IsUint(type)) return left.Uint > right.Uint;
            if (type == ScalarType.Double) return left.Double > right.Double;
            if (HLSLTypeUtils.IsFloat(type)) return left.Float > right.Float;
            if (type == ScalarType.Bool) return (left.Bool ? 1 : 0) > (right.Bool ? 1 : 0);
            throw new InvalidOperationException();
        }

        private static RawValue LessEqual(ScalarType type, RawValue left, RawValue right)
        {
            if (type == ScalarType.Float) return left.Float <= right.Float;
            if (HLSLTypeUtils.IsInt(type)) return left.Int <= right.Int;
            if (HLSLTypeUtils.IsUint(type)) return left.Uint <= right.Uint;
            if (type == ScalarType.Double) return left.Double <= right.Double;
            if (HLSLTypeUtils.IsFloat(type)) return left.Float <= right.Float;
            if (type == ScalarType.Bool) return (left.Bool ? 1 : 0) <= (right.Bool ? 1 : 0);
            throw new InvalidOperationException();
        }

        private static RawValue GreaterEqual(ScalarType type, RawValue left, RawValue right)
        {
            if (type == ScalarType.Float) return left.Float >= right.Float;
            if (HLSLTypeUtils.IsInt(type)) return left.Int >= right.Int;
            if (HLSLTypeUtils.IsUint(type)) return left.Uint >= right.Uint;
            if (type == ScalarType.Double) return left.Double >= right.Double;
            if (HLSLTypeUtils.IsFloat(type)) return left.Float >= right.Float;
            if (type == ScalarType.Bool) return (left.Bool ? 1 : 0) >= (right.Bool ? 1 : 0);
            throw new InvalidOperationException();
        }

        private static RawValue Equal(ScalarType type, RawValue left, RawValue right)
        {
            if (type == ScalarType.Float) return left.Float == right.Float;
            if (HLSLTypeUtils.IsInt(type)) return left.Int == right.Int;
            if (HLSLTypeUtils.IsUint(type)) return left.Uint == right.Uint;
            if (type == ScalarType.Double) return left.Double == right.Double;
            if (HLSLTypeUtils.IsFloat(type)) return left.Float == right.Float;
            if (type == ScalarType.Bool) return left.Bool == right.Bool;
            if (type == ScalarType.Char) return left.Char == right.Char;
            return left.Long == right.Long;
        }

        private static RawValue NotEqual(ScalarType type, RawValue left, RawValue right)
        {
            if (type == ScalarType.Float) return left.Float != right.Float;
            if (HLSLTypeUtils.IsInt(type)) return left.Int != right.Int;
            if (HLSLTypeUtils.IsUint(type)) return left.Uint != right.Uint;
            if (type == ScalarType.Double) return left.Double != right.Double;
            if (HLSLTypeUtils.IsFloat(type)) return left.Float != right.Float;
            if (type == ScalarType.Bool) return left.Bool != right.Bool;
            if (type == ScalarType.Char) return left.Char != right.Char;
            return left.Long != right.Long;
        }
        #endregion

        #region Binary and unary operators
        public static NumericValue Add(NumericValue left, NumericValue right)
        {
            (left, right) = HLSLTypeUtils.Promote(left, right, false);
            return BinOp(left, right, left.Type, Add);
        }

        public static NumericValue Mul(NumericValue left, NumericValue right)
        {
            (left, right) = HLSLTypeUtils.Promote(left, right, false);
            return BinOp(left, right, left.Type, Mul);
        }

        public static NumericValue Sub(NumericValue left, NumericValue right)
        {
            (left, right) = HLSLTypeUtils.Promote(left, right, false);
            return BinOp(left, right, left.Type, Sub);
        }

        public static NumericValue Div(NumericValue left, NumericValue right)
        {
            (left, right) = HLSLTypeUtils.Promote(left, right, false);
            return BinOp(left, right, left.Type, Div);
        }

        public static NumericValue Mod(NumericValue left, NumericValue right)
        {
            (left, right) = HLSLTypeUtils.Promote(left, right, false);
            return BinOp(left, right, left.Type, Mod);
        }

        public static NumericValue BitSHL(NumericValue left, NumericValue right)
        {
            (left, right) = HLSLTypeUtils.Promote(left, right, true);
            return BinOp(left, right, left.Type, BitSHL);
        }

        public static NumericValue BitSHR(NumericValue left, NumericValue right)
        {
            (left, right) = HLSLTypeUtils.Promote(left, right, true);
            return BinOp(left, right, left.Type, BitSHR);
        }

        public static NumericValue BitAnd(NumericValue left, NumericValue right)
        {
            (left, right) = HLSLTypeUtils.Promote(left, right, true);
            return BinOp(left, right, left.Type, BitAnd);
        }

        public static NumericValue BitOr(NumericValue left, NumericValue right)
        {
            (left, right) = HLSLTypeUtils.Promote(left, right, true);
            return BinOp(left, right, left.Type, BitOr);
        }

        public static NumericValue BitXor(NumericValue left, NumericValue right)
        {
            (left, right) = HLSLTypeUtils.Promote(left, right, true);
            return BinOp(left, right, left.Type, BitXor);
        }

        public static NumericValue BoolAnd(NumericValue left, NumericValue right)
        {
            (left, right) = HLSLTypeUtils.Promote(left.Cast(ScalarType.Bool), right.Cast(ScalarType.Bool), false);
            return BinOp(left, right, ScalarType.Bool, BoolAnd);
        }

        public static NumericValue BoolOr(NumericValue left, NumericValue right)
        {
            (left, right) = HLSLTypeUtils.Promote(left.Cast(ScalarType.Bool), right.Cast(ScalarType.Bool), false);
            return BinOp(left, right, ScalarType.Bool, BoolOr);
        }

        public static NumericValue Negate(NumericValue left)
        {
            ScalarType t = left.Type;
            var res = left.Map(x => Negate(t, x));
            if (res.Type == ScalarType.Uint)
                return res.Cast(ScalarType.Int);
            return res;
        }

        public static NumericValue BoolNegate(NumericValue left)
        {
            return left.Cast(ScalarType.Bool).Map(BoolNegate);
        }

        public static NumericValue BitNot(NumericValue left)
        {
            ScalarType t = left.Type;
            return left.Map(x => BitNot(t, x));
        }

        public static NumericValue Less(NumericValue left, NumericValue right)
        {
            (left, right) = HLSLTypeUtils.Promote(left, right, false);
            return BinOp(left, right, left.Type, Less).Cast(ScalarType.Bool);
        }

        public static NumericValue Greater(NumericValue left, NumericValue right)
        {
            (left, right) = HLSLTypeUtils.Promote(left, right, false);
            return BinOp(left, right, left.Type, Greater).Cast(ScalarType.Bool);
        }

        public static NumericValue LessEqual(NumericValue left, NumericValue right)
        {
            (left, right) = HLSLTypeUtils.Promote(left, right, false);
            return BinOp(left, right, left.Type, LessEqual).Cast(ScalarType.Bool);
        }

        public static NumericValue GreaterEqual(NumericValue left, NumericValue right)
        {
            (left, right) = HLSLTypeUtils.Promote(left, right, false);
            return BinOp(left, right, left.Type, GreaterEqual).Cast(ScalarType.Bool);
        }

        public static NumericValue Equal(NumericValue left, NumericValue right)
        {
            (left, right) = HLSLTypeUtils.Promote(left, right, false);
            return BinOp(left, right, left.Type, Equal).Cast(ScalarType.Bool);
        }

        public static NumericValue NotEqual(NumericValue left, NumericValue right)
        {
            (left, right) = HLSLTypeUtils.Promote(left, right, false);
            return BinOp(left, right, left.Type, NotEqual).Cast(ScalarType.Bool);
        }
        #endregion
    }
}
