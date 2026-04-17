#include "../HLSLTest.hlsl"

// Tests inspired by DXC tools/clang/test/HLSLFileCheck/hlsl/operators/

// ============================================================================
// SHIFT OPERATOR MASKING
// Inspired by DXC operators/binary/shift.hlsl and shift-mask.hlsl.
// HLSL (and the underlying x86 ISA) masks the shift amount to the lower 5 bits
// for 32-bit integers (effective range 0–31) and lower 6 bits for 64-bit.
// Shifting a 32-bit value by 32 is the same as shifting by 0; by 33 == by 1; etc.
// ============================================================================

[Test]
void Shift_LeftShift_By32_EqualsBy0()
{
    int x = 1;
    int r = x << 32; // 32 & 0x1F == 0 → shift by 0
    ASSERT(r == 1);
}

[Test]
void Shift_LeftShift_By33_EqualsBy1()
{
    int x = 1;
    int r = x << 33; // 33 & 0x1F == 1 → shift by 1
    ASSERT(r == 2);
}

[Test]
void Shift_LeftShift_By50_EqualsBy18()
{
    int x = 1;
    int r = x << 50; // 50 & 0x1F == 18 → shift by 18
    ASSERT(r == (1 << 18));
}

[Test]
void Shift_UintLeftShift_By32_EqualsBy0()
{
    uint x = 1u;
    uint r = x << 32u;
    ASSERT(r == 1u);
}

[Test]
void Shift_RightShift_By32_EqualsBy0()
{
    int x = 0x10;
    int r = x >> 32; // 32 & 0x1F == 0 → no shift
    ASSERT(r == 0x10);
}

[Test]
void Shift_RightShift_By50_EqualsBy18()
{
    int x = (1 << 18) * 4; // bit 19
    int r = x >> 50;       // 50 & 0x1F == 18
    ASSERT(r == 4);
}

// ============================================================================
// TERNARY OPERATOR WITH MATRIX OPERANDS
// Inspired by DXC operators/ternary-return-matrix.hlsl
// The ternary operator must work correctly when both branches are matrices.
// ============================================================================

[Test]
void Ternary_Matrix_TrueBranch()
{
    float2x2 a = float2x2(1, 2, 3, 4);
    float2x2 b = float2x2(5, 6, 7, 8);
    float2x2 r = true ? a : b;
    ASSERT(r[0][0] == 1 && r[0][1] == 2);
    ASSERT(r[1][0] == 3 && r[1][1] == 4);
}

[Test]
void Ternary_Matrix_FalseBranch()
{
    float2x2 a = float2x2(1, 2, 3, 4);
    float2x2 b = float2x2(5, 6, 7, 8);
    float2x2 r = false ? a : b;
    ASSERT(r[0][0] == 5 && r[0][1] == 6);
    ASSERT(r[1][0] == 7 && r[1][1] == 8);
}

[Test]
void Ternary_Matrix_ResultUsedInArithmetic()
{
    float2x2 a = float2x2(1, 0, 0, 1); // identity
    float2x2 b = float2x2(2, 0, 0, 2); // 2x scale
    bool flag = true;
    float2x2 r = (flag ? a : b);
    // r should be identity; r[0][0] + r[1][1] == 2
    ASSERT(r[0][0] + r[1][1] == 2.0);
}

// ============================================================================
// PRE/POST INCREMENT AND DECREMENT
// Inspired by DXC operators/unary/increment_decrement_locals.hlsl and
// operators/unary/vector_increment_decrement.hlsl
// Post-increment returns the old value; pre-increment returns the new value.
// ============================================================================

[Test]
void Increment_PostIncrement_ReturnsOldValue()
{
    int x = 10;
    int r = x++;
    ASSERT(r == 10); // returned old value
    ASSERT(x == 11); // x was incremented
}

[Test]
void Increment_PostDecrement_ReturnsOldValue()
{
    int x = 10;
    int r = x--;
    ASSERT(r == 10);
    ASSERT(x == 9);
}

[Test]
void Increment_PreIncrement_ReturnsNewValue()
{
    int x = 10;
    int r = ++x;
    ASSERT(r == 11);
    ASSERT(x == 11);
}

[Test]
void Increment_PreDecrement_ReturnsNewValue()
{
    int x = 10;
    int r = --x;
    ASSERT(r == 9);
    ASSERT(x == 9);
}

[Test]
void Increment_PreIncrement_ChainedFour()
{
    // --(++(++(++x))) starting from 10: +4, then -1 = 13... wait:
    // ++(++x): x→12, then outer -->: x stays 12... no.
    // Actually chained pre-increments:
    // ++x: x=11, returns ref to x
    // ++(++x): x=12, returns ref to x
    // ++(++(++x)): x=13, returns ref to x
    // --(++(++(++x))): x=12, returns ref to x
    int x = 10;
    int r = --(++(++(++x)));
    ASSERT(r == 12);
    ASSERT(x == 12);
}

[Test]
void Increment_Vector_PostIncrement_ReturnsOldValue()
{
    int2 v = int2(5, 10);
    int2 r = v++;
    ASSERT(r.x == 5 && r.y == 10);
    ASSERT(v.x == 6 && v.y == 11);
}

[Test]
void Increment_Vector_PreDecrement_ReturnsNewValue()
{
    int2 v = int2(5, 10);
    int2 r = --v;
    ASSERT(r.x == 4 && r.y == 9);
    ASSERT(v.x == 4 && v.y == 9);
}

[Test]
void Increment_Matrix_PostIncrement_ReturnsOldValue()
{
    float1x1 m;
    m[0][0] = 10.0;
    float1x1 r = m++;
    ASSERT(r[0][0] == 10.0);
    ASSERT(m[0][0] == 11.0);
}

[Test]
void Increment_Matrix_PreIncrement_ReturnsNewValue()
{
    float2x2 m = float2x2(1, 2, 3, 4);
    float2x2 r = ++m;
    ASSERT(r[0][0] == 2.0 && r[0][1] == 3.0);
    ASSERT(r[1][0] == 4.0 && r[1][1] == 5.0);
    ASSERT(m[0][0] == 2.0);
}

// ============================================================================
// FLOAT MODULO (fmod semantics)
// Inspired by DXC operators/binary/fmodPs.hlsl
// The % operator on floats uses IEEE remainder (fmod), not integer truncated
// division. fmod(a, b) = a - b * trunc(a/b).
// ============================================================================

[Test]
void Fmod_PositiveDividend_PositiveDivisor()
{
    float r = 7.0 % 3.0;
    ASSERT(abs(r - 1.0) < 0.001); // 7 - 3*2 = 1
}

[Test]
void Fmod_FractionalValues()
{
    float r = 2.5 % 1.5;
    ASSERT(abs(r - 1.0) < 0.001); // 2.5 - 1.5*1 = 1.0
}

[Test]
void Fmod_NegativeDividend_PositiveDivisor()
{
    // fmod(-7, 3) = -7 - 3*trunc(-7/3) = -7 - 3*(-2) = -7 + 6 = -1
    float r = -7.0 % 3.0;
    ASSERT(abs(r - (-1.0)) < 0.001);
}

[Test]
void Fmod_Vector_ComponentWise()
{
    float3 a = float3(7.0, 2.5, -7.0);
    float3 b = float3(3.0, 1.5,  3.0);
    float3 r = a % b;
    ASSERT(abs(r.x - 1.0)  < 0.001);
    ASSERT(abs(r.y - 1.0)  < 0.001);
    ASSERT(abs(r.z - (-1.0)) < 0.001);
}
