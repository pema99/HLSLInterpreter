# HLSLInterpreter 🚧🛠️
A experimental library for interpreting HLSL shader code on the CPU. The primary usecase is to run automated tests for shaders, which run entirely on the CPU. The interpreter is relatively self contained, and can also be used for other applications that want to run shader code.

[Click here to try the web demo](https://pema.dev/hlsl/)

## Basic usage

To get started, make a `HLSLRunner` object, and feed in some HLSL code:
```cs
string hlslCode = "float add(float a, float b) { return a + b; }";
HLSLRunner runner = new HLSLRunner();
runner.ProcessCode(hlslCode);
```
Then you can use `HLSLRunner.CallFunction()` to call HLSL functions:
```cs
HLSLValue result = runner.CallFunction("add", (NumericValue)1, (NumericValue)3);
Console.WriteLine(result); // Prints "4"
```
Alternatively, you can use `HLSLRunner.RunTests()` to automatically find and run HLSL functions marked with the `[Test]` attribute as tests. For more information, check the next section.

For more advanced usages, check the API exposed by by [`HLSLRunner`](https://github.com/pema99/HLSLInterpreter/blob/master/HLSLInterpreter/HLSLRunner.cs). It serves as the main entry point for the interpreter.

## Shader testing
Shader test functions can be written either in dedicated test files, or directly inline inside existing shaders.

Here's an example of a test:

```hlsl
#include "HLSLTest.hlsl"

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
To run it, you can use `HLSLRunner.RunTests()`, optionally passing a string to filter test names with.

> Note: You must include [HLSLTest.hlsl](https://github.com/pema99/HLSLInterpreter/blob/master/HLSLInterpreter/HLSLTest.hlsl) in your shader for tests to compile properly.

For some more examples, check the test files in [this folder](https://github.com/pema99/HLSLInterpreter/tree/master/HLSLInterpreter.Tests/Shaders). The section about [advanced testing features](#advanced-testing-features) shows more interesting things you can do in tests.

## Feature overview
I estimate that the interpreter supports around 80% of the HLSL language, though several features are still missing. Here's a rough overview of what works:
- Every arithmetic [operator](https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/dx-graphics-hlsl-operators) including casts.
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
- Groupshared memory.
- Type aliases via `typedef`.
- Texture and Buffer types.

## Limitations
The main limitation of the interpreter is that it is very slow - think hundreds or thousands of time slower than running on a GPU. The interpreter is written primarily with correctness in mind, and I've made no attempt to optimize it more than necessary. Don't expect to run interesting shaders at high resolutions without waiting several seconds for a frame! The thread count is configurable, and most usecases will want to run just a few threads.

Here is a list of features I have yet to implement:
- Legacy texture functions like `tex2D()` and `tex3D()`.

There might be some more things I missed, and the library is still very much work in progress - bugs be plenty!

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

To test code that reads from or writes to textures and buffers, you can define a **mock struct** in HLSL that backs the resource. The interpreter calls into this struct for every read and write.

A mock struct can implement any of the following methods:

| Method | Purpose |
|--------|---------|
| `void Initialize()` | Called once when the mock is created. Use it to fill initial data. |
| `T Read(int x, int y, int z, int w, int mipLevel)` | Called for every texel read. |
| `void Write(int x, int y, int z, int w, int mipLevel, T value)` | Called for every texel write. |
| `int SizeX()` / `int SizeY()` / `int SizeZ()` | Return the resource dimensions. |
| `int MipCount()` | Returns the mip level count. |

There are two ways to attach a mock to a resource.

**`[MockResource]` on a test parameter** - the test runner creates and injects a fresh mock before each test call. This is the preferred style when the resource is a test input:

```hlsl
struct MockTex2D
{
    int width;
    int height;
    float4 data[16];

    void Initialize()
    {
        width = 4;
        height = 4;
        for (int i = 0; i < 16; i++)
            data[i] = float4(i, 0, 0, 1);
    }

    float4 Read(int x, int y, int z, int w, int mipLevel) { return data[y * width + x]; }
    void Write(int x, int y, int z, int w, int mipLevel, float4 value) { data[y * width + x] = value; }
};

[Test]
void Texture_Load([MockResource(MockTex2D)] RWTexture2D<float4> tex)
{
    // pixel (2,1): index = 1*4+2 = 6
    float4 val = tex.Load(int2(2, 1));
    ASSERT(val.x == 6.0);
}
```

`[MockResource]` parameters can be combined with `[TestCase]`:

```hlsl
[Test]
[TestCase(0, 0)]
[TestCase(2, 1)]
void Texture_LoadAtCoord([MockResource(MockTex2D)] RWTexture2D<float4> tex, int x, int y)
{
    float4 val = tex.Load(int2(x, y));
    ASSERT(val.x == float(y * 4 + x));
}
```

**`MOCK_RESOURCE(resource, MockStructType)`** - binds a mock to a globally declared resource variable at the point of the call. Use this when the resource is a shader global rather than a function parameter:

```hlsl
RWTexture2D<float4> g_tex;

[Test]
void Texture_Write_Global()
{
    MOCK_RESOURCE(g_tex, MockTex2D);
    g_tex[int2(3, 0)] = float4(77, 0, 0, 1);
    float4 val = g_tex.Load(int2(3, 0));
    ASSERT(val.x == 77.0);
}
```
