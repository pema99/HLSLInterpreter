// Tests for the test framework assertions: ASSERT_EQUAL, ASSERT_NEAR,
// ASSERT_UNIFORM, ASSERT_VARYING, and [Ignore].

[Test]
void TestFramework_AssertEqual_Scalars()
{
    ASSERT_EQUAL(1 + 1, 2);
    ASSERT_EQUAL(3.0 * 4.0, 12.0);
    ASSERT_EQUAL(-7, -7);
    ASSERT_EQUAL(true, true);
}

[Test]
void TestFramework_AssertEqual_Vectors()
{
    ASSERT_EQUAL(float3(1, 2, 3), float3(1, 2, 3));
    ASSERT_EQUAL(float2(0, 0), float2(0, 0));

    float4 a = float4(1, 0, 0, 1);
    float4 b = float4(1, 0, 0, 1);
    ASSERT_EQUAL(a, b);
}

[Test]
void TestFramework_AssertNear_Scalars()
{
    ASSERT_NEAR(sqrt(2.0), 1.41421356, 0.0001);
    ASSERT_NEAR(sin(0.0), 0.0, 0.0001);
    ASSERT_NEAR(cos(0.0), 1.0, 0.0001);
    ASSERT_NEAR(exp(1.0), 2.71828182, 0.0001);
}

[Test]
void TestFramework_AssertNear_Vectors()
{
    // normalize(1,1,1) should equal (1/sqrt(3), 1/sqrt(3), 1/sqrt(3))
    float3 result = normalize(float3(1, 1, 1));
    float invSqrt3 = 0.57735027;
    ASSERT_NEAR(result, float3(invSqrt3, invSqrt3, invSqrt3), 0.0001);
}

[Test]
[WarpSize(2, 2)]
void TestFramework_AssertUniform()
{
    // Literals are uniform (SGPR)
    ASSERT_UNIFORM(42.0);
    ASSERT_UNIFORM(2 + 3);

    // Variables assigned from uniform expressions are uniform
    float x = 7.5;
    ASSERT_UNIFORM(x);

    // Uniform arithmetic stays uniform
    ASSERT_UNIFORM(x * 2.0 + 1.0);
}

[Test]
[WarpSize(2, 2)]
void TestFramework_AssertVarying()
{
    // WaveGetLaneIndex() returns a different value per thread (VGPR)
    int lane = WaveGetLaneIndex();
    ASSERT_VARYING(lane);

    // Arithmetic derived from a varying value is also varying
    float scaled = lane * 3.14;
    ASSERT_VARYING(scaled);
}

[Test]
[Ignore("not implemented yet")]
void TestFramework_Ignored_WithReason()
{
    FAIL_TEST();
}

[Test]
[Ignore]
void TestFramework_Ignored_NoReason()
{
    FAIL_TEST();
}
