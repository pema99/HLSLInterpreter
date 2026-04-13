
// ---- Declaration without a body ----

SamplerState DefaultSampler;
SamplerComparisonState DefaultComparisonSampler;

// ---- Modern D3D10+ block syntax ----

SamplerState LinearWrapSampler
{
    Filter   = MIN_MAG_MIP_LINEAR;
    AddressU = Wrap;
    AddressV = Wrap;
};

SamplerState PointClampSampler
{
    Filter   = MIN_MAG_MIP_POINT;
    AddressU = Clamp;
    AddressV = Clamp;
    AddressW = Clamp;
};

SamplerState AnisotropicSampler
{
    Filter        = ANISOTROPIC;
    MaxAnisotropy = 8;
    AddressU      = Wrap;
    AddressV      = Wrap;
};

SamplerState LodSampler
{
    Filter = MIN_MAG_MIP_LINEAR;
    MinLOD = 2.0;
    MaxLOD = 6.0;
};

SamplerComparisonState ShadowSampler
{
    Filter         = COMPARISON_MIN_MAG_MIP_LINEAR;
    ComparisonFunc = LESS_EQUAL;
    AddressU       = Border;
    AddressV       = Border;
};

// ---- Legacy sampler_state syntax (D3D9) ----

SamplerState LegacyLinearSampler =
sampler_state
{
    MipFilter = LINEAR;
    MinFilter = LINEAR;
    MagFilter = LINEAR;
};

SamplerState LegacyPointSampler =
sampler_state
{
    MipFilter = POINT;
    MinFilter = POINT;
    MagFilter = POINT;
};

SamplerState LegacyWithTexture =
sampler_state
{
    Texture   = <DefaultSampler>;
    MinFilter = LINEAR;
    MagFilter = LINEAR;
    MipFilter = NONE;
    AddressU  = Wrap;
    AddressV  = Wrap;
};

// ---- Legacy `sampler` type (equivalent to SamplerState) ----

sampler LegacySamplerType =
sampler_state
{
    MipFilter = LINEAR;
    MinFilter = LINEAR;
    MagFilter = LINEAR;
};

// All global declarations above are exercised on file load.
[Test]
void SamplerState_GlobalDeclarations_CanDeclare() {}

// ---- All fields set ----

[Test]
void SamplerState_AllFields_CanDeclare()
{
    SamplerState s
    {
        Filter         = MIN_MAG_MIP_POINT;
        AddressU       = Wrap;
        AddressV       = Mirror;
        AddressW       = Clamp;
        MinLOD         = 1.0;
        MaxLOD         = 9.0;
        MipLODBias     = 0.5;
        MaxAnisotropy  = 4;
        ComparisonFunc = LESS;
        BorderColor    = float4(0.0, 0.0, 0.0, 1.0);
    };
}

// ---- Local / inline declarations ----

[Test]
void SamplerState_NoBody_CanDeclare()
{
    SamplerState s;
}

[Test]
void SamplerComparisonState_NoBody_CanDeclare()
{
    SamplerComparisonState s;
}

[Test]
void SamplerState_LocalDeclaration_WithBody_CanDeclare()
{
    SamplerState s
    {
        Filter   = MIN_MAG_MIP_POINT;
        AddressU = Clamp;
    };
}

[Test]
void SamplerState_LocalLegacySyntax_CanDeclare()
{
    SamplerState s = sampler_state { MinFilter = LINEAR; MagFilter = LINEAR; MipFilter = LINEAR; };
}

// ============================================================
// Sampler-field behavioral tests
//
// Texture layout (MockSampler1D, 4 texels, single mip):
//   texel[x] = x  →  [0, 1, 2, 3]
//
// UV → texelPos mapping (size=4, lod=0):  texelPos = UV*4 - 0.5
//   UV=0.125 → 0.0  (centre of texel 0)
//   UV=0.375 → 1.0  (centre of texel 1)
//   UV=0.5   → 1.5  (midpoint between texels 1 and 2)
//   UV=0.625 → 2.0  (centre of texel 2)
//   UV=0.875 → 3.0  (centre of texel 3)
//
// Multi-mip layout (MockSampler1DMip, 4 texels, 3 mips):
//   mip 0 (size 4):  texel[x] = x        →  [0,1,2,3]
//   mip 1 (size 2):  texel[x] = 10+x     →  [10,11]
//   mip 2 (size 1):  texel[0] = 20
// ============================================================

struct MockSampler1D
{
    int SizeX()    { return 4; }
    int MipCount() { return 1; }
    void Initialize() {}
    float4 Read(int x, int y, int z, int w, int mipLevel) { return float4(float(x), 0, 0, 1); }
};

struct MockSampler1DMip
{
    int SizeX()    { return 4; }
    int MipCount() { return 3; }
    void Initialize() {}
    float4 Read(int x, int y, int z, int w, int mipLevel)
    {
        if (mipLevel == 0) return float4(float(x), 0, 0, 1);
        if (mipLevel == 1) return float4(10.0 + float(x), 0, 0, 1);
        return float4(20.0, 0, 0, 1);
    }
};

Texture1D<float4> g_samplerTex;

// Filter = POINT: texelPos=1.5 → round(1.5)=2 → texel[2]=2.0
// (Linear would give lerp(1,2,0.5)=1.5)
[Test]
void Sampler_Filter_Point()
{
    MOCK_RESOURCE(g_samplerTex, MockSampler1D);
    SamplerState s { Filter = MIN_MAG_MIP_POINT; AddressU = Clamp; };
    float4 val = g_samplerTex.SampleLevel(s, 0.5, 0.0);
    ASSERT(val.x == 2.0);
}

// Filter = LINEAR: texelPos=1.5 → lerp(texel[1]=1, texel[2]=2, 0.5) = 1.5
[Test]
void Sampler_Filter_Linear()
{
    MOCK_RESOURCE(g_samplerTex, MockSampler1D);
    SamplerState s { Filter = MIN_MAG_MIP_LINEAR; AddressU = Clamp; };
    float4 val = g_samplerTex.SampleLevel(s, 0.5, 0.0);
    ASSERT(val.x == 1.5);
}

// Filter = LINEAR + AddressU = Wrap: UV=0.9375 → texelPos=3.25
// baseTexel=3 (frac=0.25), neighbor=4 wraps to 0 → lerp(texel[3]=3, texel[0]=0, 0.25) = 2.25
// (Without bilinear wrap fix, neighbor 4 would clamp to 3, giving lerp(3,3,0.25)=3.0)
[Test]
void Sampler_Filter_Linear_Wrap()
{
    MOCK_RESOURCE(g_samplerTex, MockSampler1D);
    SamplerState s { Filter = MIN_MAG_MIP_LINEAR; AddressU = Wrap; };
    float4 val = g_samplerTex.SampleLevel(s, 0.9375, 0.0);
    ASSERT(val.x == 2.25);
}

// AddressU = Wrap: UV=1.375 → frac(1.375)=0.375 → texelPos=1.0 → texel[1]=1.0
[Test]
void Sampler_Address_Wrap()
{
    MOCK_RESOURCE(g_samplerTex, MockSampler1D);
    SamplerState s { Filter = MIN_MAG_MIP_POINT; AddressU = Wrap; };
    float4 val = g_samplerTex.SampleLevel(s, 1.375, 0.0);
    ASSERT(val.x == 1.0);
}

// AddressU = Clamp: UV=2.0 → clamped to 1.0 → texelPos=3.5 → round→4→clamp→3 → texel[3]=3.0
[Test]
void Sampler_Address_Clamp()
{
    MOCK_RESOURCE(g_samplerTex, MockSampler1D);
    SamplerState s { Filter = MIN_MAG_MIP_POINT; AddressU = Clamp; };
    float4 val = g_samplerTex.SampleLevel(s, 2.0, 0.0);
    ASSERT(val.x == 3.0);
}

// AddressU = Mirror: UV=1.375 → mirrored to 0.625 → texelPos=2.0 → texel[2]=2.0
// Formula: 1 - abs(frac(uv/2)*2 - 1) = 1 - abs(frac(0.6875)*2 - 1) = 1 - 0.375 = 0.625
[Test]
void Sampler_Address_Mirror()
{
    MOCK_RESOURCE(g_samplerTex, MockSampler1D);
    SamplerState s { Filter = MIN_MAG_MIP_POINT; AddressU = Mirror; };
    float4 val = g_samplerTex.SampleLevel(s, 1.375, 0.0);
    ASSERT(val.x == 2.0);
}

// AddressU = Border: UV outside [0,1] → returns zero (border colour).
// In-bounds UV=0.375 still returns texel[1]=1.0.
[Test]
void Sampler_Address_Border()
{
    MOCK_RESOURCE(g_samplerTex, MockSampler1D);
    SamplerState s { Filter = MIN_MAG_MIP_POINT; AddressU = Border; };
    float4 out_val = g_samplerTex.SampleLevel(s, 1.5, 0.0);
    ASSERT(out_val.x == 0.0);
    float4 in_val = g_samplerTex.SampleLevel(s, 0.375, 0.0);
    ASSERT(in_val.x == 1.0);
}

// MipLODBias=1: explicit lod=0 becomes effective lod=1.
// At lod=1, size=2 → texelPos=0.375*2-0.5=0.25 → round(0.25)=0 → mip-1 texel[0]=10.0
[Test]
void Sampler_MipLodBias()
{
    MOCK_RESOURCE(g_samplerTex, MockSampler1DMip);
    SamplerState s { Filter = MIN_MAG_MIP_POINT; AddressU = Clamp; MipLODBias = 1.0; };
    float4 val = g_samplerTex.SampleLevel(s, 0.375, 0.0);
    ASSERT(val.x == 10.0);
}

// MinLOD=1: explicit lod=0 clamped up to 1 → same mip-1 result.
[Test]
void Sampler_MinLod()
{
    MOCK_RESOURCE(g_samplerTex, MockSampler1DMip);
    SamplerState s { Filter = MIN_MAG_MIP_POINT; AddressU = Clamp; MinLOD = 1.0; };
    float4 val = g_samplerTex.SampleLevel(s, 0.375, 0.0);
    ASSERT(val.x == 10.0);
}

// MaxLOD=0: explicit lod=1 clamped down to 0.
// At lod=0, size=4 → texelPos=1.0 → round(1.0)=1 → mip-0 texel[1]=1.0
[Test]
void Sampler_MaxLod()
{
    MOCK_RESOURCE(g_samplerTex, MockSampler1DMip);
    SamplerState s { Filter = MIN_MAG_MIP_POINT; AddressU = Clamp; MaxLOD = 0.0; };
    float4 val = g_samplerTex.SampleLevel(s, 0.375, 1.0);
    ASSERT(val.x == 1.0);
}

// ============================================================
// SampleCmp family
// ============================================================

// Depth texture: texel[x] = x * 0.25  →  [0, 0.25, 0.5, 0.75]
struct MockSampler1DDepth
{
    int SizeX()    { return 4; }
    int MipCount() { return 1; }
    void Initialize() {}
    float4 Read(int x, int y, int z, int w, int mipLevel)
    {
        return float4(float(x) * 0.25, 0, 0, 1);
    }
};

// ComparisonFunc=LESS: UV=0.375 → texelPos=1.0 → point → depth=texel[1]=0.25
// 0.25 < 0.5 → 1.0 ;  0.25 < 0.1 → 0.0
[Test]
void Sampler_SampleCmpLevelZero_Comparison()
{
    MOCK_RESOURCE(g_samplerTex, MockSampler1DDepth);
    SamplerComparisonState cs { ComparisonFunc = LESS; Filter = COMPARISON_MIN_MAG_MIP_POINT; AddressU = Clamp; };
    float pass_val = g_samplerTex.SampleCmpLevelZero(cs, 0.375, 0.5);
    ASSERT(pass_val == 1.0);
    float fail_val = g_samplerTex.SampleCmpLevelZero(cs, 0.375, 0.1);
    ASSERT(fail_val == 0.0);
}

// AddressU=Wrap: UV=1.375 wraps to 0.375 → depth=0.25 → 0.25 < 0.5 → 1.0
// (Clamp would give texel[3]=0.75 → 0.75 < 0.5 → 0.0, so the modes are distinguishable)
[Test]
void Sampler_SampleCmpLevelZero_AddressWrap()
{
    MOCK_RESOURCE(g_samplerTex, MockSampler1DDepth);
    SamplerComparisonState cs { ComparisonFunc = LESS; Filter = COMPARISON_MIN_MAG_MIP_POINT; AddressU = Wrap; };
    float val = g_samplerTex.SampleCmpLevelZero(cs, 1.375, 0.5);
    ASSERT(val == 1.0);
}

// ============================================================
// Gather and GatherCmp families
// ============================================================

// 2D 4x4: texel(x,y) = y*4+x  →  values [0..15]
struct MockSampler2D
{
    int SizeX() { return 4; }
    int SizeY() { return 4; }
    void Initialize() {}
    float4 Read(int x, int y, int z, int w, int mipLevel)
    {
        return float4(float(y * 4 + x), 0, 0, 1);
    }
};

// 2D 4x4 uniform depth: all texels = 0.5
struct MockSampler2DDepth
{
    int SizeX() { return 4; }
    int SizeY() { return 4; }
    void Initialize() {}
    float4 Read(int x, int y, int z, int w, int mipLevel) { return float4(0.5, 0, 0, 1); }
};

Texture2D<float4> g_samplerTex2D;

// AddressU=Wrap: UV.x=1.25 wraps to 0.25 → same footprint as UV=(0.25, 0.25).
// HLSL Gather output order: .x=(u0,v1), .y=(u1,v1), .z=(u1,v0), .w=(u0,v0)
// baseX=0, baseY=0 → corners: (0,1)=4, (1,1)=5, (1,0)=1, (0,0)=0
[Test]
void Sampler_Gather_AddressWrap()
{
    MOCK_RESOURCE(g_samplerTex2D, MockSampler2D);
    SamplerState s { Filter = MIN_MAG_MIP_POINT; AddressU = Wrap; AddressV = Clamp; };
    float4 val = g_samplerTex2D.Gather(s, float2(1.25, 0.25));
    ASSERT(val.x == 4.0);
    ASSERT(val.y == 5.0);
    ASSERT(val.z == 1.0);
    ASSERT(val.w == 0.0);
}

// ComparisonFunc=LESS_EQUAL: all depth=0.5, cmpVal=0.6 → 0.5<=0.6 → 1.0 for every corner.
[Test]
void Sampler_GatherCmp_Comparison()
{
    MOCK_RESOURCE(g_samplerTex2D, MockSampler2DDepth);
    SamplerComparisonState cs { ComparisonFunc = LESS_EQUAL; };
    float4 val = g_samplerTex2D.GatherCmp(cs, float2(0.25, 0.25), 0.6);
    ASSERT(val.x == 1.0);
    ASSERT(val.y == 1.0);
    ASSERT(val.z == 1.0);
    ASSERT(val.w == 1.0);
}

// ============================================================
// CalculateLevelOfDetail family
// ============================================================

// WarpSize(2,2): threads 0,2 UV=0, threads 1,3 UV=0.5  (tid%2 gives X-lane index)
// ddx(UV * SizeX=4) = 0.5*4 = 2, ddy = 0  →  rho=2  →  LOD = log2(2) = 1.0
[Test]
[WarpSize(2, 2)]
void Sampler_CalculateLevelOfDetail()
{
    MOCK_RESOURCE(g_samplerTex, MockSampler1D);
    SamplerState s { Filter = MIN_MAG_MIP_LINEAR; };
    uint tid = WaveGetLaneIndex();
    float lod = g_samplerTex.CalculateLevelOfDetail(s, float(tid % 2) * 0.5);
    ASSERT(lod == 1.0);
}

[Test]
[WarpSize(2, 2)]
void Sampler_CalculateLevelOfDetailUnclamped()
{
    MOCK_RESOURCE(g_samplerTex, MockSampler1D);
    SamplerState s { Filter = MIN_MAG_MIP_LINEAR; };
    uint tid = WaveGetLaneIndex();
    float lod = g_samplerTex.CalculateLevelOfDetailUnclamped(s, float(tid % 2) * 0.5);
    ASSERT(lod == 1.0);
}
