#include "../HLSLTest.hlsl"

// Tests inspired by DXC tools/clang/test/HLSLFileCheck/hlsl/functions/overloading/

// ============================================================================
// INTRINSIC SHADOWING
// Inspired by intrinsic_shadowing.hlsl: a user-defined function with the same
// name as a built-in intrinsic completely shadows it within the same scope.
// ============================================================================

// User-defined abs that always returns 42.
int abs(int x) { return 42; }

[Test]
void Overload_IntrinsicShadowing_UserAbsShadowsBuiltin()
{
    int result = abs(-5);
    ASSERT(result == 42);
}

[Test]
void Overload_IntrinsicShadowing_UserAbsCalledForPositive()
{
    // The user's abs is chosen regardless of sign — it always returns 42.
    int result = abs(5);
    ASSERT(result == 42);
}

// ============================================================================
// EXTENDING INTRINSICS WITH USER OVERLOADS
// Inspired by intrinsic_overloading.hlsl: adding a new overload for a type not
// handled by the built-in, while the built-in still works for its own types.
// ============================================================================

struct MyNum { float val; };

// New overload for a struct type — the built-in has no such overload.
float abs(MyNum s) { return abs(s.val) + 100.0; }

[Test]
void Overload_ExtendIntrinsic_UserOverloadForStruct()
{
    MyNum n;
    n.val = -3.0;
    float result = abs(n);
    ASSERT(abs(result - 103.0) < 0.001); // 100 + abs(-3)
}

[Test]
void Overload_ExtendIntrinsic_BuiltinStillWorksForFloat()
{
    // The built-in abs for float must still be reachable.
    float result = abs(-7.5);
    ASSERT(abs(result - 7.5) < 0.001);
}

[Test]
void Overload_ExtendIntrinsic_BuiltinStillWorksForInt()
{
    // The built-in abs for int — but our user abs(int) shadows it!
    // Because abs(int) is declared above, abs(-3) calls the user version.
    int result = abs(-3);
    ASSERT(result == 42);
}

// ============================================================================
// TRICKY FUNCTION OVERLOADING — DIFFERENT PARAM COUNTS
// Functions with the same name but different numbers of parameters.
// ============================================================================

float Compute(float a)           { return a; }
float Compute(float a, float b)  { return a + b; }
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
// When calling f(1.0), the float overload should win over int.
// When calling f(1), the int overload should win over float (exact match).
// ============================================================================

int  Typed(int x)   { return x * 2; }
float Typed(float x) { return x * 3.0; }

[Test]
void Overload_TypePromotion_IntArgPicksIntOverload()
{
    int r = Typed(5);
    ASSERT(r == 10); // 5 * 2 (int overload)
}

[Test]
void Overload_TypePromotion_FloatArgPicksFloatOverload()
{
    float r = Typed(4.0);
    ASSERT(abs(r - 12.0) < 0.001); // 4.0 * 3.0 (float overload)
}

// ============================================================================
// OVERLOAD RESOLUTION WITH VECTORS
// Choosing the right vector overload based on argument shape.
// ============================================================================

float VecOp(float2 v)  { return v.x + v.y; }
float VecOp(float3 v)  { return v.x + v.y + v.z; }
float VecOp(float4 v)  { return v.x + v.y + v.z + v.w; }

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
// RECURSIVE SHADOWING: CALLING THE ORIGINAL INTRINSIC FROM A WRAPPER
// The wrapper uses a different name for the inner call, but verifies that
// a user function with the intrinsic's name can still call the original via
// a cast path (or by forwarding to a renamed wrapper).
// ============================================================================

// Helper that calls the real built-in sin via a different argument path.
float my_sin(float x) { return sin(x); }

float sin(int n)
{
    // Shadow sin(float) for integer arguments; computes n * pi/6.
    return my_sin(n * 0.5235987); // n * (pi/6)
}

[Test]
void Overload_ShadowSin_IntArgCallsUserVersion()
{
    // sin(1) should use the user-defined int overload: sin(pi/6) ≈ 0.5
    float result = sin(1);
    ASSERT(abs(result - 0.5) < 0.01);
}

[Test]
void Overload_ShadowSin_FloatArgCallsBuiltin()
{
    // sin(0.0) should call the built-in (no int overload matches 0.0f).
    float result = sin(0.0);
    ASSERT(abs(result) < 0.001);
}
