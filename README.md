# HLSLInterpreter
A library for interpreting HLSL shader code on the CPU. The primary usecase is to run automated tests for shaders, which run entirely on the CPU. The interpreter is relatively self contained, and can also be used for other applications that want to run shader code.

## Shader testing
Shader test functions can be written either in dedicated test files, or directly inline inside existing shaders. The API exposed by `HLSLRunner` provides a way to discover and run tests across a list of files.

Here's an example of a test:

```hlsl
#include HLSLTest.hlsl

float SphereSDF(float3 position, float radius)
{
    return length(position) - radius;
}

[Test]
void SphereSDF_SignIsCorrect()
{
    // Point outside sphere - distance is positive
    float signedDistance = SphereSDF(float3(1,1,1), 0.5);
    ASSERT(signedDistance > 0);

    // Point inside sphere - distance is negative
    signedDistance = SphereSDF(float3(0.4, 0.0, 0.0), 0.5);
    ASSERT(signedDistance < 0);
}
```

For some more examples, check the files in [this folder](https://github.com/pema99/HLSLInterpreter/tree/master/HLSLInterpreter.Tests/Shaders). The section about [advanced testing features](#advanced-testing-features) shows more interesting things you can do in tests.

## Feature overview
I estimate that the interpreter supports around 80% of the HLSL language, though several features are still missing. Here's a rough overview of what works:
- Every arithmetic [operator](https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/dx-graphics-hlsl-operators).
- Every [intrinsic](https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/dx-graphics-hlsl-intrinsic-functions) which can be mapped reasonably to CPU execution.
  - This includes intrinsics that require simulating multiple threads, like `ddx()/ddy()` and wave intrinsics.
- All kinds of control flow, including loops and conditionals.
  - This includes support for control flow with conditions that vary between threads (aka.anyt divergent control flow).
- Variable declarations and assignments.
- Struct type declarations and usage.
  - Including struct member functions and variables. 
- All builtin scalar, vector and matrix types.
- Vector and matrix swizzling.
- Function declarations, calls and function overloading.
- `in`, `out` and `inout` parameters.
- Arrays and array indexing.
- Namespaces.
- Preprocessor directives and macros.

## Limitations
The main limitation of the interpreter is that it is very slow - think hundreds or thousands of time slower. The interpreter is written primarily with correctness in mind, and I've made no attempt to optimize it more than necessary. Don't expect to run interesting shaders at high resolutions without waiting several seconds for a frame! The thread count is configurable, and most usecases will want to run just a few threads.

Here is a list of features I have yet to implement:
- Type aliases using `typedef`.
- Casts with array types, like `(float[4])float4(1,2,3,4)`, or `(int[7])myFloatArray`.
- Interface definitions and struct inheritance.
- Inline struct definitions in variable declarations.
- SamplerState declarations (`SamplerState mySampler = {...};`)
- Most texture/buffer types beyond basic `Texture2D` (no `Gather*`, `SampleCmp`, `SampleBias`, `SampleGrad`, etc.)
- Writes to texture and buffer types like `myTexture[int2(2,3)] = float4(1,2,3,4);`.
- Legacy texture functions like `tex2D()` and `tex3D()`.
- Assigning array literals to vectors like `float3 foo = {1,2,3}`. Same for matrices.
- `this` keyword.

There might be some more things I missed...

## Advanced testing features
This section will show a few more advanced features you can use when writing tests. The testing API is still a work in progress, more to come.

Aside from the basic `[Test]` attribute, you can also use `[TestCase]` to make parametric tests:

```hlsl
[Test]
[TestCase(float3(1,2,3), float3(4,5,6))]
[TestCase(float3(1,1,1), float3(1,1,2))]
void VectorsAreDifferent(float3 a, float3 b)
{
    ASSERT(any(a != b));
}
```

If you want to manually control when a test fails or passes, you can use `PASS_TEST()` and `FAIL_TEST()` macros.

```hlsl
[Test]
void AlwaysPass()
{
    if (1 == 1) // Water is wet
        PASS_TEST();
    else
        FAIL_TEST();
}
```

The `ASSERT_MSG()` macro can used to attach an error message to an assert.

```hlsl
[Test]
void AssertSomeMsg()
{
    ASSERT_MSG(false, "Oh no! The assert has fired!");
}
```

If you just want to log some information from a test, you can use `PRINTF()`:

```hlsl
[Test]
void PrintTheNumber()
{
    int theNumber = 42;
    PRINTF("The number is %d!", 42);
}
```

If you need to control the size of the warp/wavefront used to run the test, you can use the `WarpSize()` intrinsic. This is useful when writing tests that use `ddx()/ddy()` or wave intrinsics:

```hlsl
[Test]
[WarpSize(2, 2)] // 2x2 warp
void TestWaveActiveSum()
{
    int index = WaveGetLaneIndex();
    ASSERT(WaveActiveSum(index) == 6); // 0+1+2+3
    ASSERT(ddx(index) == 1); // 1-0=1, 3-2=1
}
```
