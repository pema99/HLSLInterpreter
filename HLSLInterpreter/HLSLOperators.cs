using System;
using UnityShaderParser.HLSL;

namespace HLSL
{
    public static class HLSLOperators
    {
        #region Helpers for binary and unary operators
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

        private static RawValue BoolAnd(RawValue left, RawValue right)
        {
            return left.Bool && right.Bool;
        }

        private static RawValue BoolOr(RawValue left, RawValue right)
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
            ScalarType t = left.Type;
            return HLSLValueUtils.Map2(left, right, (l, r) => Add(t, l, r));
        }

        public static NumericValue Mul(NumericValue left, NumericValue right)
        {
            (left, right) = HLSLTypeUtils.Promote(left, right, false);
            ScalarType t = left.Type;
            return HLSLValueUtils.Map2(left, right, (l, r) => Mul(t, l, r));
        }

        public static NumericValue Sub(NumericValue left, NumericValue right)
        {
            (left, right) = HLSLTypeUtils.Promote(left, right, false);
            ScalarType t = left.Type;
            return HLSLValueUtils.Map2(left, right, (l, r) => Sub(t, l, r));
        }

        public static NumericValue Div(NumericValue left, NumericValue right)
        {
            (left, right) = HLSLTypeUtils.Promote(left, right, false);
            ScalarType t = left.Type;
            return HLSLValueUtils.Map2(left, right, (l, r) => Div(t, l, r));
        }

        public static NumericValue Mod(NumericValue left, NumericValue right)
        {
            (left, right) = HLSLTypeUtils.Promote(left, right, false);
            ScalarType t = left.Type;
            return HLSLValueUtils.Map2(left, right, (l, r) => Mod(t, l, r));
        }

        public static NumericValue BitSHL(NumericValue left, NumericValue right)
        {
            (left, right) = HLSLTypeUtils.Promote(left, right, true);
            ScalarType t = left.Type;
            return HLSLValueUtils.Map2(left, right, (l, r) => BitSHL(t, l, r));
        }

        public static NumericValue BitSHR(NumericValue left, NumericValue right)
        {
            (left, right) = HLSLTypeUtils.Promote(left, right, true);
            ScalarType t = left.Type;
            return HLSLValueUtils.Map2(left, right, (l, r) => BitSHR(t, l, r));
        }

        public static NumericValue BitAnd(NumericValue left, NumericValue right)
        {
            (left, right) = HLSLTypeUtils.Promote(left, right, true);
            ScalarType t = left.Type;
            return HLSLValueUtils.Map2(left, right, (l, r) => BitAnd(t, l, r));
        }

        public static NumericValue BitOr(NumericValue left, NumericValue right)
        {
            (left, right) = HLSLTypeUtils.Promote(left, right, true);
            ScalarType t = left.Type;
            return HLSLValueUtils.Map2(left, right, (l, r) => BitOr(t, l, r));
        }

        public static NumericValue BitXor(NumericValue left, NumericValue right)
        {
            (left, right) = HLSLTypeUtils.Promote(left, right, true);
            ScalarType t = left.Type;
            return HLSLValueUtils.Map2(left, right, (l, r) => BitXor(t, l, r));
        }

        public static NumericValue BoolAnd(NumericValue left, NumericValue right)
        {
            (left, right) = HLSLTypeUtils.Promote(left.Cast(ScalarType.Bool), right.Cast(ScalarType.Bool), false);
            return HLSLValueUtils.Map2(left, right, BoolAnd);
        }

        public static NumericValue BoolOr(NumericValue left, NumericValue right)
        {
            (left, right) = HLSLTypeUtils.Promote(left.Cast(ScalarType.Bool), right.Cast(ScalarType.Bool), false);
            return HLSLValueUtils.Map2(left, right, BoolOr);
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
            ScalarType t = left.Type;
            return HLSLValueUtils.Map2(left, right, (l, r) => Less(t, l, r)).Cast(ScalarType.Bool);
        }

        public static NumericValue Greater(NumericValue left, NumericValue right)
        {
            (left, right) = HLSLTypeUtils.Promote(left, right, false);
            ScalarType t = left.Type;
            return HLSLValueUtils.Map2(left, right, (l, r) => Greater(t, l, r)).Cast(ScalarType.Bool);
        }

        public static NumericValue LessEqual(NumericValue left, NumericValue right)
        {
            (left, right) = HLSLTypeUtils.Promote(left, right, false);
            ScalarType t = left.Type;
            return HLSLValueUtils.Map2(left, right, (l, r) => LessEqual(t, l, r)).Cast(ScalarType.Bool);
        }

        public static NumericValue GreaterEqual(NumericValue left, NumericValue right)
        {
            (left, right) = HLSLTypeUtils.Promote(left, right, false);
            ScalarType t = left.Type;
            return HLSLValueUtils.Map2(left, right, (l, r) => GreaterEqual(t, l, r)).Cast(ScalarType.Bool);
        }

        public static NumericValue Equal(NumericValue left, NumericValue right)
        {
            (left, right) = HLSLTypeUtils.Promote(left, right, false);
            ScalarType t = left.Type;
            return HLSLValueUtils.Map2(left, right, (l, r) => Equal(t, l, r)).Cast(ScalarType.Bool);
        }

        public static NumericValue NotEqual(NumericValue left, NumericValue right)
        {
            (left, right) = HLSLTypeUtils.Promote(left, right, false);
            ScalarType t = left.Type;
            return HLSLValueUtils.Map2(left, right, (l, r) => NotEqual(t, l, r)).Cast(ScalarType.Bool);
        }
        #endregion
    }
}
