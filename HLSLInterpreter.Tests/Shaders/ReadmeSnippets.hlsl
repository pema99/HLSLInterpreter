// Validates that every HLSL code snippet shown in README.md compiles and behaves correctly.

// ============================================================
// Shared helpers
// ============================================================

float SphereSDF(float3 position, float radius)
{
    return length(position) - radius;
}

// ============================================================
// Running tests
// ============================================================

[Test]
void Readme_SphereSDF_SignIsCorrect()
{
    // Point outside sphere - distance is positive
    float signedDistance = SphereSDF(float3(1,1,1), 0.5);
    ASSERT(signedDistance > 0);

    // Point inside sphere - distance is negative
    signedDistance = SphereSDF(float3(0.4, 0.0, 0.0), 0.5);
    ASSERT(signedDistance < 0);
}

// ============================================================
// Basic test assertions
// ============================================================

[Test]
void Readme_BasicAssert()
{
    ASSERT(1 + 1 == 2);
    ASSERT(sqrt(4.0) == 2.0);
}

[Test]
void Readme_AssertSomeMsg()
{
    float result = sqrt(4.0);
    ASSERT_MSG(result == 2.0, "Expected sqrt(4) to be 2");
}

[Test]
void Readme_CheckResult()
{
    float3 result = normalize(float3(1, 0, 0));
    ASSERT_EQUAL(result, float3(1, 0, 0));
}

[Test]
void Readme_CheckApproximateResult()
{
    ASSERT_NEAR(sqrt(2.0), 1.41421356, 0.0001);
    ASSERT_NEAR(normalize(float3(1,1,1)), float3(0.57735027, 0.57735027, 0.57735027), 0.0001);
}

// ============================================================
// Special test assertions
// ============================================================

[Test]
void Readme_AlwaysPass()
{
    if (1 == 1) // Water is wet
        PASS_TEST;
    else
        FAIL_TEST;
}

[Test]
void Readme_CheckSign()
{
    if (sqrt(4.0) == 2.0)
        PASS_TEST_MSG("sqrt is exact");
    else
        FAIL_TEST_MSG("unexpected sqrt result");
}

[Test]
[TestCase(2)]
[TestCase(3)]
void Readme_OnlyRunForEven(int n)
{
    if (n % 2 != 0)
        IGNORE_TEST_MSG("skipping odd input");
    ASSERT(n % 2 == 0);
}

[Test]
[Ignore("floor() precision not implemented yet")]
void Readme_CheckFloorEdgeCase()
{
    ASSERT_EQUAL(floor(-0.0), 0.0);
}

[Test]
[WarpSize(2, 2)] // 2x2 warp = 4 threads
void Readme_CheckUniformity()
{
    // Constants and expressions computed without per-thread input are uniform
    ASSERT_UNIFORM(42.0);

    // WaveGetLaneIndex() returns a different value on every thread
    int lane = WaveGetLaneIndex();
    ASSERT_VARYING(lane);
}

// ============================================================
// Printing and logging
// ============================================================

[Test]
void Readme_PrintTheNumber()
{
    int theNumber = 42;
    PRINTF("The number is %d!", theNumber);
}

[Test]
[WarpSize(2, 2)]
void Readme_PrintPerThread()
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

// ============================================================
// Test case generation
// ============================================================

[Test]
[TestCase(float3(1,2,3), float3(4,5,6))]
[TestCase(float3(1,1,1), float3(1,1,2))]
void Readme_VectorsAreDifferent(float3 a, float3 b)
{
    ASSERT(any(a != b));
}

void Readme_GenerateCases()
{
    TEST_CASE(1, 1.0);
    TEST_CASE(2, 4.0);
    TEST_CASE(3, 9.0);
}

[Test]
[TestCaseSource("Readme_GenerateCases")]
void Readme_CheckSquareRoot(int n, float expected)
{
    ASSERT_NEAR(sqrt(expected), float(n), 0.0001);
}

void Readme_GenerateScales()
{
    TEST_VALUE(1);
    TEST_VALUE(2);
}

[Test]
void Readme_ScaleIsPositive([ValueSource("Readme_GenerateScales")] int scale, [Values(0.5, 1.0, 2.0)] float x)
{
    ASSERT(x * scale > 0);
}

// ============================================================
// Test metadata
// ============================================================

[Test]
[Description("Verifies the sign of the SDF at points inside and outside the sphere")]
[Category("SDF")]
void Readme_SphereSDF_SignIsCorrect_WithMeta()
{
    ASSERT(SphereSDF(float3(1,1,1), 0.5) > 0);
    ASSERT(SphereSDF(float3(0.1, 0, 0), 0.5) < 0);
}

[Test]
[TestCase(0)]
[TestCase(1)]
void Readme_LogCurrentTest(int x)
{
    PRINTF("Running %s with x=%d", TEST_NAME, x);
}

// ============================================================
// Mocking resources
// ============================================================

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
void Readme_Texture_Load([MockResource(MockTex2D)] RWTexture2D<float4> tex)
{
    // pixel (2,1): index = 1*4+2 = 6
    float4 val = tex.Load(int2(2, 1));
    ASSERT(val.x == 6.0);
}

[Test]
[TestCase(0, 0)]
[TestCase(2, 1)]
void Readme_Texture_LoadAtCoord([MockResource(MockTex2D)] RWTexture2D<float4> tex, int x, int y)
{
    float4 val = tex.Load(int2(x, y));
    ASSERT(val.x == float(y * 4 + x));
}

RWTexture2D<float4> g_readme_tex;

[Test]
void Readme_Texture_Write_Global()
{
    MOCK_RESOURCE(g_readme_tex, MockTex2D);
    g_readme_tex[int2(3, 0)] = float4(77, 0, 0, 1);
    float4 val = g_readme_tex.Load(int2(3, 0));
    ASSERT(val.x == 77.0);
}
