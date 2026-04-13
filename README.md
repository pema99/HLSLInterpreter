# HLSLInterpreter
A experimental library for interpreting HLSL shader code on the CPU. The library includes a framework for creating automated tests for shaders, which run entirely on the CPU.

The interpreter is relatively self contained, and can also be used for other applications that want to run shader code, such as [this fancy web demo](https://pema.dev/hlsl/).

## Table of contents
- [Basic usage](#basic-usage)
- [Feature overview](#feature-overview)
- [Shader testing framework](#shader-testing-framework)
  - [Running tests](#running-tests)
  - [Basic test assertions](#basic-test-assertions)
  - [Special test assertions](#special-test-assertions)
  - [Printing and logging](#printing-and-logging)
  - [Test case generation](#test-case-generation)
  - [Test metadata](#test-metadata)
  - [Mocking resources](#mocking-resources)
  - [Test framework cheatsheet](#test-framework-cheatsheet)
- [Limitations](#limitations)

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
Alternatively, you can use `HLSLRunner.RunTests()` to automatically find and run HLSL functions marked with the `[Test]` attribute as tests. See the [Shader testing framework](#shader-testing-framework) section for more info.

For more advanced usages, check the API exposed by by [`HLSLRunner`](https://github.com/pema99/HLSLInterpreter/blob/master/HLSLInterpreter/HLSLRunner.cs). It serves as the main entry point for the interpreter.

## Feature overview
The interpreter supports the majority of the HLSL language, though a few niche features are missing or don't translate to CPU execution. Here's a rough overview of what works:
- Every arithmetic [operator](https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/dx-graphics-hlsl-operators) including casts.
- Every [intrinsic](https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/dx-graphics-hlsl-intrinsic-functions) which can be mapped reasonably to CPU execution.
  - This includes intrinsics that require simulating multiple threads, like `ddx()/ddy()` and wave intrinsics.
- All kinds of control flow, including loops and conditionals.
  - This includes support for control flow with conditions that vary between threads (aka. divergent control flow).
- Variable declarations and assignments.
- Struct type declarations and usage.
  - Including struct member functions and variables. 
- All builtin scalar, vector and matrix types.
- Vector and matrix swizzling.
- Function declarations, calls and function overloading.
- `in`, `out` and `inout` parameters.
- Arrays and array indexing.
- Preprocessor directives and macros.
- Groupshared memory.
- Type aliases via `typedef`.
- Texture and Buffer types.
- Interfaces and struct inheritance.
- Namespaces.

## Shader testing framework

This section describes the shader testing framework included in the library. For some real examples of shader tests, check the test files in [this folder](https://github.com/pema99/HLSLInterpreter/tree/master/HLSLInterpreter.Tests/Shaders).

### Running tests
Shader test functions can be written either in dedicated test files, or directly inline inside existing shaders. You must include [HLSLTest.hlsl](https://github.com/pema99/HLSLInterpreter/blob/master/HLSLInterpreter/HLSLTest.hlsl) in your shader for tests to compile properly. Here's an example of a test:

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
To run it, load the file with `ProcessCode()` and call `RunTests()`:

```cs
HLSLRunner runner = new HLSLRunner();
runner.ProcessCode(File.ReadAllText("MyShader.hlsl"));

foreach (var result in runner.RunTests())
{
    Console.WriteLine($"{result.TestName}: {result.Status}");
    if (result.Status == HLSLRunner.TestStatus.Fail)
        Console.WriteLine(result.Message);
}
```

You can pass a regex string to `RunTests()` to filter which tests are run by name.

> **Note:** The `ProcessCode(string)` overloads automatically define `__HLSL_TEST_RUNNER__` before parsing, which is what activates the macros in `HLSLTest.hlsl`. If you parse the code yourself (for example via `ShaderParser.ParseTopLevelDeclarations`) and pass the resulting nodes to `ProcessCode(IEnumerable<HLSLSyntaxNode>)`, you must define it manually in the `HLSLParserConfig`:
> ```cs
> var config = new HLSLParserConfig
> {
>     BasePath = Path.GetDirectoryName(filePath),
>     Defines = new Dictionary<string, string> { ["__HLSL_TEST_RUNNER__"] = "1" },
> };
> var nodes = ShaderParser.ParseTopLevelDeclarations(File.ReadAllText(filePath), config);
> runner.ProcessCode(nodes);
> ```

### Basic test assertions

`ASSERT(expr)` is the basic assertion macro. It fails the test if `expr` evaluates to false on any active thread:

```hlsl
[Test]
void BasicAssert()
{
    ASSERT(1 + 1 == 2);
    ASSERT(sqrt(4.0) == 2.0);
}
```

The `ASSERT_MSG()` macro can used to attach an error message to an assert.

```hlsl
[Test]
void AssertSomeMsg()
{
    float result = sqrt(4.0);
    ASSERT_MSG(result == 2.0, "Expected sqrt(4) to be 2");
}
```

The `ASSERT_EQUAL(a, b)` macro checks that two values are equal and prints both values in the failure message:

```hlsl
[Test]
void CheckResult()
{
    float3 result = normalize(float3(1, 0, 0));
    ASSERT_EQUAL(result, float3(1, 0, 0));
}
```

The `ASSERT_NEAR(a, b, eps)` macro checks that two values are within `eps` of each other:

```hlsl
[Test]
void CheckApproximateResult()
{
    ASSERT_NEAR(sqrt(2.0), 1.41421356, 0.0001);
    ASSERT_NEAR(normalize(float3(1,1,1)), float3(0.57735027, 0.57735027, 0.57735027), 0.0001);
}
```

### Special test assertions

If you want to manually control when a test fails or passes, you can use `PASS_TEST` and `FAIL_TEST` macros.

```hlsl
[Test]
void AlwaysPass()
{
    if (1 == 1) // Water is wet
        PASS_TEST;
    else
        FAIL_TEST;
}
```

The `_MSG` variants take an additional string argument that is shown in the test runner output:

```hlsl
[Test]
void CheckSign()
{
    if (sqrt(4.0) == 2.0)
        PASS_TEST_MSG("sqrt is exact");
    else
        FAIL_TEST_MSG("unexpected sqrt result");
}
```

`IGNORE_TEST` and `IGNORE_TEST_MSG()` skip the test at runtime from within the test body. This is useful when the skip condition can only be evaluated at runtime. The `[Ignore]` attribute is the unconditional alternative, which skips the test regardless, and accepts an optional reason string:

```hlsl
[Test]
[TestCase(2)]
[TestCase(3)]
void OnlyRunForEven(int n)
{
    if (n % 2 != 0)
        IGNORE_TEST_MSG("skipping odd input");
    ASSERT(n % 2 == 0);
}
```

```hlsl
[Test]
[Ignore("floor() precision not implemented yet")]
void CheckFloorEdgeCase()
{
    ASSERT_EQUAL(floor(-0.0), 0.0);
}
```

The `ASSERT_UNIFORM(expr)` and `ASSERT_VARYING(expr)` macros check whether a value is stored in a scalar register (same across all threads) or vector register (differs per thread). These are most useful in tests that use multiple threads. To control the number of threads, use the `[WarpSize(x, y)]` attribute to set the size of the warp used for running the test.

```hlsl
[Test]
[WarpSize(2, 2)] // 2x2 warp = 4 threads
void CheckUniformity()
{
    // Constants and expressions computed without per-thread input are uniform
    ASSERT_UNIFORM(42.0);

    // WaveGetLaneIndex() returns a different value on every thread
    int lane = WaveGetLaneIndex();
    ASSERT_VARYING(lane);
}
```

### Printing and logging

`PRINTF()` logs information from a test to the console:

```hlsl
[Test]
void PrintTheNumber()
{
    int theNumber = 42;
    PRINTF("The number is %d!", theNumber);
}
```

When called with varying data or inside divergent control flow, `PRINTF()` prints once per active thread, prefixing each line with the thread index. This makes it straight forward to inspect per-thread values:

```hlsl
[Test]
[WarpSize(2, 2)]
void PrintPerThread()
{
    // Varying data. Each thread passes a different value to PRINTF
    int lane = WaveGetLaneIndex();
    PRINTF("lane=%d", lane);

    // Divergent control flow. Only threads where lane is even reach this PRINTF
    if (lane % 2 == 0)
    {
        PRINTF("an even thread reached this point");
    }
}
```

The snippet will produce the following output:

```
[Thread 0] lane=0
[Thread 1] lane=1
[Thread 2] lane=2
[Thread 3] lane=3
[Thread 0] an even thread reached this point
[Thread 2] an even thread reached this point
```

### Test case generation

Aside from the basic `[Test]` attribute, you can use `[TestCase]` to make parametric tests:

```hlsl
[Test]
[TestCase(float3(1,2,3), float3(4,5,6))]
[TestCase(float3(1,1,1), float3(1,1,2))]
void VectorsAreDifferent(float3 a, float3 b)
{
    ASSERT(any(a != b));
}
```

`[TestCaseSource]` is like `[TestCase]`, but the cases are generated by calling an HLSL function. Inside the generator, use `TEST_CASE()` to emit each case:

```hlsl
void GenerateCases()
{
    TEST_CASE(1, 1.0);
    TEST_CASE(2, 4.0);
    TEST_CASE(3, 9.0);
}

[Test]
[TestCaseSource("GenerateCases")]
void CheckSquareRoot(int n, float expected)
{
    ASSERT_NEAR(sqrt(expected), float(n), 0.0001);
}
```

`[Values]` and `[ValueSource]` are parameter-level attributes that generate test cases combinatorially. `[Values]` takes a list of inline values. `[ValueSource]` calls a generator function that emits values with `TEST_VALUE()`. Every combination of values across all parameters is run as a separate test:

```hlsl
void GenerateScales()
{
    TEST_VALUE(1);
    TEST_VALUE(2);
}

[Test]
void ScaleIsPositive([ValueSource("GenerateScales")] int scale, [Values(0.5, 1.0, 2.0)] float x)
{
    ASSERT(x * scale > 0);
}
```

This generates 6 test cases — one for every combination of `scale` and `x`:

```
ScaleIsPositive(1, 0.5)
ScaleIsPositive(1, 1)
ScaleIsPositive(1, 2)
ScaleIsPositive(2, 0.5)
ScaleIsPositive(2, 1)
ScaleIsPositive(2, 2)
```

### Test metadata

`[Description]` and `[Category]` attach metadata to a test function. The description and category are surfaced in the test runner output and can be used for filtering or reporting:

```hlsl
float SphereSDF(float3 pos, float r) { return length(pos) - r; }

[Test]
[Description("Verifies the sign of the SDF at points inside and outside the sphere")]
[Category("SDF")]
void SphereSDF_SignIsCorrect()
{
    ASSERT(SphereSDF(float3(1,1,1), 0.5) > 0);
    ASSERT(SphereSDF(float3(0.1, 0, 0), 0.5) < 0);
}
```

`TEST_NAME` returns the name of the currently running test as a string.

```hlsl
[Test]
[TestCase(0)]
[TestCase(1)]
void LogCurrentTest(int x)
{
    PRINTF("Running %s with x=%d", TEST_NAME, x);
}
```

### Mocking resources

To test code that reads from or writes to textures and buffers, you can define a **mock struct** in HLSL that backs the resource. The interpreter calls into this struct for every read and write.

A mock struct can implement any of the following methods, all of which are optional:

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

`[MockResource]` parameters can be combined with `[TestCase]` and other test case generator attributes:

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

### Test framework cheatsheet

**Attributes** - applied to functions or parameters:

| Attribute | Scope | Description |
|-----------|-------|-------------|
| `[Test]` | Function | Marks a function as a test to be discovered and run. |
| `[TestCase(args...)]` | Function | Runs the test once per attribute, passing the given arguments. |
| `[TestCaseSource("Generator")]` | Function | Runs the test for each case emitted by `Generator` via `TEST_CASE()`. |
| `[Values(vals...)]` | Parameter | Provides a set of values for this parameter, combined combinatorially with other parameters. |
| `[ValueSource("Generator")]` | Parameter | Like `[Values]`, but values are emitted by `Generator` via `TEST_VALUE()`. |
| `[MockResource(MockType)]` | Parameter | Injects a mock resource of the given struct type before each test call. |
| `[WarpSize(x, y)]` | Function | Sets the warp size for the test. Required for tests using wave intrinsics or `ddx()/ddy()`. |
| `[Ignore]`<br>`[Ignore("reason")]` | Function | Unconditionally skips the test, with an optional reason shown in the output. |
| `[Description("text")]` | Function | Attaches a human-readable description to the test. |
| `[Category("name")]` | Function | Assigns the test to a category for filtering and reporting. |

**Macros** - called from inside test function bodies:

| Macro | Description |
|-------|-------------|
| `ASSERT(expr)`<br>`ASSERT_MSG(expr, msg)` | Fails if `expr` is false on any active thread. |
| `ASSERT_EQUAL(a, b)` | Fails if `a != b`, printing both values in the failure message. |
| `ASSERT_NEAR(a, b, eps)` | Fails if `\|a - b\| > eps`. |
| `ASSERT_UNIFORM(expr)` | Fails if `expr` is stored in a vector register (may differ across threads). |
| `ASSERT_VARYING(expr)` | Fails if `expr` is stored in a uniform register (same across all threads). |
| `PASS_TEST`<br>`PASS_TEST_MSG(msg)` | Immediately passes the test. |
| `FAIL_TEST`<br>`FAIL_TEST_MSG(msg)` | Immediately fails the test. |
| `IGNORE_TEST`<br>`IGNORE_TEST_MSG(msg)` | Skips the test at runtime. |
| `PRINTF(fmt, ...)` | Prints to the console. Prints once per active thread when data is varying. |
| `TEST_NAME` | Returns the name of the currently running test as a string. |
| `TEST_CASE(args...)` | Emits a test case from a `[TestCaseSource]` generator function. |
| `TEST_VALUE(val)` | Emits a single value from a `[ValueSource]` generator function. |
| `MOCK_RESOURCE(resource, MockType)` | Binds a mock struct to a global resource variable. |

## Limitations
The main limitation of the interpreter is that it is very slow - think hundreds or thousands of time slower than running on a GPU. The interpreter is written primarily with correctness in mind, and I've made no attempt to optimize it more than necessary. Don't expect to run interesting shaders at high resolutions without waiting several seconds for a frame! The thread count is configurable, and most usecases will want to run just a few threads.

The interpreter is capable of simulating 1 warp/wavefront of arbitrary size. If you need multiple warps, you can use multiple instances of the interpreter ([example](https://github.com/pema99/HLSLInterpreter/blob/7a5ae52c439afe29fd38b20cf2589e16dba03325/HLSLInterpreter.Examples/Program.cs#L124)). If run in parallel, beware that atomic operations and barriers are no-ops, so you'll have to manually handle synchronization if multiple CPU threads access the same memory.

The library is still a work in progress, so bugs be plenty.
