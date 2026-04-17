#include "../HLSLTest.hlsl"

// Tests inspired by DXC tools/clang/test/HLSLFileCheck/hlsl/functions/overloading/

// ============================================================================
// INTRINSIC SHADOWING
// Inspired by intrinsic_shadowing.hlsl: a user-defined function with the same
// name as a built-in intrinsic completely shadows it within the same scope.
// ============================================================================

// Shadow clamp (3-arg) instead of a 1-arg intrinsic so the shadowed name
// doesn't conflict with other overloading tests further down in this file.
int clamp(int x, int lo, int hi) { return 42; }

[Test]
void Overload_IntrinsicShadowing_UserClampShadowsBuiltin()
{
    int result = clamp(5, 1, 10);
    ASSERT(result == 42);
}

[Test]
void Overload_IntrinsicShadowing_UserClampCalledRegardlessOfRange()
{
    // Even an out-of-range value returns 42 since the user function wins.
    int result = clamp(-100, 0, 50);
    ASSERT(result == 42);
}

// ============================================================================
// EXTENDING INTRINSICS WITH USER OVERLOADS
// Inspired by intrinsic_overloading.hlsl: adding a new overload for a type not
// handled by the built-in, while the built-in still works for its own types.
// Since no user overload matches float arguments, abs(-7.5) hits the built-in.
// ============================================================================

struct MyNum { float val; };

// Extend abs for a struct type — the built-in has no such overload.
float abs(MyNum s) { return s.val < 0.0 ? -s.val : s.val; }

[Test]
void Overload_ExtendIntrinsic_UserOverloadForStruct()
{
    MyNum n;
    n.val = -3.0;
    float result = abs(n);
    ASSERT(abs(result - 3.0) < 0.001);
}

[Test]
void Overload_ExtendIntrinsic_BuiltinStillWorksForFloat()
{
    // No user overload matches a plain float, so the built-in is reached.
    float result = abs(-7.5);
    ASSERT(abs(result - 7.5) < 0.001);
}

[Test]
void Overload_ExtendIntrinsic_BuiltinStillWorksForInt()
{
    int result = abs(-3);
    ASSERT(result == 3);
}

// ============================================================================
// TRICKY FUNCTION OVERLOADING — DIFFERENT PARAM COUNTS
// ============================================================================

float Compute(float a)                   { return a; }
float Compute(float a, float b)          { return a + b; }
float Compute(float a, float b, float c) { return a + b + c; }

[Test]
void Overload_DifferentParamCount_OneArg()
{
    ASSERT(Compute(5.0) == 5.0);
}

[Test]
void Overload_DifferentParamCount_TwoArgs()
{
    ASSERT(Compute(3.0, 4.0) == 7.0);
}

[Test]
void Overload_DifferentParamCount_ThreeArgs()
{
    ASSERT(Compute(1.0, 2.0, 3.0) == 6.0);
}

// ============================================================================
// OVERLOAD RESOLUTION WITH TYPE PROMOTION
// ============================================================================

int   Typed(int x)   { return x * 2; }
float Typed(float x) { return x * 3.0; }

[Test]
void Overload_TypePromotion_IntArgPicksIntOverload()
{
    int r = Typed(5);
    ASSERT(r == 10);
}

[Test]
void Overload_TypePromotion_FloatArgPicksFloatOverload()
{
    float r = Typed(4.0);
    ASSERT(abs(r - 12.0) < 0.001);
}

// ============================================================================
// OVERLOAD RESOLUTION WITH DIFFERENT VECTOR SIZES
// ============================================================================

float VecOp(float2 v) { return v.x + v.y; }
float VecOp(float3 v) { return v.x + v.y + v.z; }
float VecOp(float4 v) { return v.x + v.y + v.z + v.w; }

[Test]
void Overload_VectorShape_Float2PicksFloat2Overload()
{
    ASSERT(VecOp(float2(1.0, 2.0)) == 3.0);
}

[Test]
void Overload_VectorShape_Float3PicksFloat3Overload()
{
    ASSERT(VecOp(float3(1.0, 2.0, 3.0)) == 6.0);
}

[Test]
void Overload_VectorShape_Float4PicksFloat4Overload()
{
    ASSERT(VecOp(float4(1.0, 2.0, 3.0, 4.0)) == 10.0);
}

// ============================================================================
// SHADOWING A TRANSCENDENTAL INTRINSIC FOR A SPECIFIC TYPE
// A user-defined sin(int) computes n * pi/6 and shadows the float built-in
// only for integer arguments; sin(0.0f) still reaches the built-in.
// ============================================================================

// sin(int n) shadows the built-in for integer args only.
// Approximates sin(n * pi/6) using Taylor expansion (valid for small x):
// sin(x) ≈ x - x³/6 + x⁵/120  with x = n*(pi/6)
float sin(int n)
{
    float x = n * 0.5235987; // n * (pi/6)
    return x - (x*x*x)/6.0 + (x*x*x*x*x)/120.0;
}

[Test]
void Overload_ShadowSin_IntArgCallsUserVersion()
{
    float result = sin(1); // user: sin(pi/6) ≈ 0.5
    ASSERT(abs(result - 0.5) < 0.01);
}

[Test]
void Overload_ShadowSin_FloatArgCallsBuiltin()
{
    float result = sin(0.0);
    ASSERT(abs(result) < 0.001);
}
