#include "../HLSLTest.hlsl"

// Tests inspired by DXC tools/clang/test/HLSLFileCheck/hlsl/intrinsics/
// Focusing on edge cases not already covered by Intrinsics.hlsl.

// ============================================================================
// ABS AND SIGN FOR UNSIGNED INTEGER TYPES
// Inspired by DXC intrinsics/basic/intrinsic_uabs_usign.hlsl
// abs(uint) must return the value unchanged — no sign reinterpretation.
// sign(uint) must return 1 for nonzero, 0 for zero — never -1.
// ============================================================================

[Test]
void EdgeCase_Abs_Uint_MaxValue_Unchanged()
{
    uint u = 0xFFFFFFFFu;
    uint r = (uint)abs(u);
    ASSERT(r == 0xFFFFFFFFu);
}

[Test]
void EdgeCase_Abs_Uint_Zero_ReturnsZero()
{
    uint u = 0u;
    uint r = (uint)abs(u);
    ASSERT(r == 0u);
}

[Test]
void EdgeCase_Abs_Uint_SmallValue_Unchanged()
{
    uint u = 7u;
    uint r = (uint)abs(u);
    ASSERT(r == 7u);
}

[Test]
void EdgeCase_Sign_Uint_Positive_ReturnsOne()
{
    uint u = 5u;
    int r = sign(u);
    ASSERT(r == 1);
}

[Test]
void EdgeCase_Sign_Uint_MaxValue_ReturnsOne()
{
    uint u = 0xFFFFFFFFu;
    int r = sign(u);
    ASSERT(r == 1); // must not return -1
}

[Test]
void EdgeCase_Sign_Uint_Zero_ReturnsZero()
{
    uint u = 0u;
    int r = sign(u);
    ASSERT(r == 0);
}

[Test]
void EdgeCase_Sign_Uint_Vector_NeverNegativeOne()
{
    uint3 v = uint3(0u, 1u, 0xFFFFFFFFu);
    int3 r = sign(v);
    ASSERT(r.x == 0);
    ASSERT(r.y == 1);
    ASSERT(r.z == 1); // must not be -1
}

// ============================================================================
// MAD FOR INTEGER AND UNSIGNED TYPES
// Inspired by DXC intrinsics/fpspecial/intrinsic5.hlsl
// The mad(m, a, b) intrinsic computes m*a + b for all numeric types,
// including int and uint, not just float.
// ============================================================================

[Test]
void EdgeCase_Mad_Int_Basic()
{
    int r = mad(2, 3, 4);
    ASSERT(r == 10); // 2*3 + 4
}

[Test]
void EdgeCase_Mad_Int_NegativeMultiplier()
{
    int r = mad(-2, 3, 10);
    ASSERT(r == 4); // -2*3 + 10 = -6 + 10
}

[Test]
void EdgeCase_Mad_Uint_Basic()
{
    uint r = mad(3u, 5u, 2u);
    ASSERT(r == 17u); // 3*5 + 2
}

[Test]
void EdgeCase_Mad_Int_Vector()
{
    int3 m = int3(1, 2, 3);
    int3 a = int3(10, 10, 10);
    int3 b = int3(1, 2, 3);
    int3 r = mad(m, a, b);
    ASSERT(r.x == 11 && r.y == 22 && r.z == 33);
}

// ============================================================================
// ISNORMAL
// Inspired by DXC intrinsics/fpspecial/intrinsic5.hlsl
// isnormal(x) returns true only for normalized (neither zero, denormal,
// infinite, nor NaN) values.
// ============================================================================

[Test]
void EdgeCase_IsNormal_NormalValues_ReturnTrue()
{
    ASSERT(isnormal(1.0));
    ASSERT(isnormal(-1.0));
    ASSERT(isnormal(1000.0));
    ASSERT(isnormal(0.001));
}

[Test]
void EdgeCase_IsNormal_Zero_ReturnsFalse()
{
    ASSERT(!isnormal(0.0));
}

[Test]
void EdgeCase_IsNormal_Inf_ReturnsFalse()
{
    ASSERT(!isnormal(1.0 / 0.0));   // +Inf
    ASSERT(!isnormal(-1.0 / 0.0));  // -Inf
}

[Test]
void EdgeCase_IsNormal_NaN_ReturnsFalse()
{
    ASSERT(!isnormal(0.0 / 0.0));
}

// ============================================================================
// ROUND — BANKER'S ROUNDING (ROUND-HALF-TO-EVEN)
// Inspired by DXC intrinsics/rounding/Round_ne_const.hlsl
// HLSL's round() uses banker's rounding (round-half-to-even), not round-half-up.
// The exact midpoint cases:
//   round(0.5)  → 0  (nearest even: 0 is even, 1 is odd)
//   round(1.5)  → 2  (nearest even: 2 is even)
//   round(2.5)  → 2  (nearest even: 2 is even)
//   round(-0.5) → 0  (nearest even)
//   round(-1.5) → -2 (nearest even)
// ============================================================================

[Test]
void Round_Banker_ZeroPointFive_RoundsToZero()
{
    ASSERT(round(0.5) == 0.0);
}

[Test]
void Round_Banker_OnePointFive_RoundsToTwo()
{
    ASSERT(round(1.5) == 2.0);
}

[Test]
void Round_Banker_TwoPointFive_RoundsToTwo()
{
    ASSERT(round(2.5) == 2.0);
}

[Test]
void Round_Banker_ThreePointFive_RoundsToFour()
{
    ASSERT(round(3.5) == 4.0);
}

[Test]
void Round_Banker_NegHalfPointFive_RoundsToZero()
{
    ASSERT(round(-0.5) == 0.0);
}

[Test]
void Round_Banker_NegOnePointFive_RoundsToNegTwo()
{
    ASSERT(round(-1.5) == -2.0);
}

[Test]
void Round_Banker_NegTwoPointFive_RoundsToNegTwo()
{
    ASSERT(round(-2.5) == -2.0);
}

[Test]
void Round_Banker_NonMidpoint_RoundsNormally()
{
    ASSERT(round(1.3) == 1.0);
    ASSERT(round(1.6) == 2.0);
    ASSERT(round(-1.3) == -1.0);
    ASSERT(round(-1.6) == -2.0);
}

[Test]
void Round_Banker_Vector()
{
    float4 v = float4(0.5, 1.5, 2.5, 3.5);
    float4 r = round(v);
    ASSERT(r.x == 0.0); // 0 is even
    ASSERT(r.y == 2.0); // 2 is even
    ASSERT(r.z == 2.0); // 2 is even
    ASSERT(r.w == 4.0); // 4 is even
}
