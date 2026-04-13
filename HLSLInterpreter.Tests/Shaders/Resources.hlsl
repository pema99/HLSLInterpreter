
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

    float4 Read(int x, int y, int z, int w, int mipLevel)
    {
        return data[y * width + x];
    }

    void Write(int x, int y, int z, int w, int mipLevel, float4 value)
    {
        data[y * width + x] = value;
    }
};

// Load from a mock texture: after Initialize, pixel (x,y) has .x == y*4+x.
[Test]
void MockResource_Load([MockResource(MockTex2D)] RWTexture2D<float4> tex)
{
    // Pixel (2, 1): index = 1*4+2 = 6 -> float4(6, 0, 0, 1)
    float4 val = tex.Load(int2(2, 1));
    ASSERT(val.x == 6.0);
    ASSERT(val.w == 1.0);
}

// Write to a mock texture via subscript, then read back.
[Test]
void MockResource_Store([MockResource(MockTex2D)] RWTexture2D<float4> tex)
{
    tex[int2(1,0)] = 99.0;
    float4 val = tex.Load(int2(1, 0));
    ASSERT(val.x == 99.0);
}

// Mock that reports non-default dimensions and mip count.
struct MockTex2D_Sized
{
    float4 data[64];

    void Initialize()
    {
        for (int i = 0; i < 64; i++)
            data[i] = float4(i, 0, 0, 1);
    }

    int SizeX()    { return 8; }
    int SizeY()    { return 8; }
    int MipCount() { return 4; }

    float4 Read(int x, int y, int z, int w, int mipLevel)
    {
        return data[y * 8 + x];
    }
};

// GetDimensions on an RWTexture2D reports width and height from SizeX/SizeY.
[Test]
void MockResource_Dimensions([MockResource(MockTex2D_Sized)] RWTexture2D<float4> tex)
{
    uint w, h;
    tex.GetDimensions(w, h);
    ASSERT(w == 8);
    ASSERT(h == 8);
}

// GetDimensions on a read-only Texture2D also reports mip count from MipCount.
[Test]
void MockResource_MipCount([MockResource(MockTex2D_Sized)] Texture2D<float4> tex)
{
    uint w, h, levels;
    tex.GetDimensions(0, w, h, levels);
    ASSERT(w == 8);
    ASSERT(h == 8);
    ASSERT(levels == 4);
}

// Load a specific pixel by coordinates supplied via TestCase.
// After Initialize, pixel (x,y) has .x == y*4+x.
[Test]
[TestCase(0, 0)]
[TestCase(2, 1)]
[TestCase(3, 3)]
void MockResource_LoadAtCoord([MockResource(MockTex2D)] RWTexture2D<float4> tex, int x, int y)
{
    float4 val = tex.Load(int2(x, y));
    ASSERT(val.x == float(y * 4 + x));
    ASSERT(val.w == 1.0);
}

// Subscript writes inside a divergent branch must only affect active threads.
// Thread 0 writes 99 to tex[0,0]. Thread 1 is inactive in that branch, but without an
// active-thread guard ResourceSubscriptWrite will write the pre-masked (original) value
// back to tex[0,0], clobbering thread 0's result.
[Test]
[WarpSize(2, 1)]
void MockResource_VaryingStore_OnlyActiveThreadsWrite([MockResource(MockTex2D)] RWTexture2D<float4> tex)
{
    uint tid = WaveGetLaneIndex();
    if (tid == 0)
    {
        tex[int2(0, 0)] = float4(99, 0, 0, 1);
    }
    // After the branch both threads are active; tex[0,0] must reflect thread 0's write.
    float4 val = tex.Load(int2(0, 0));
    ASSERT(val.x == 99.0);
}

// GetDimensions must use each thread's mip-level argument, not always thread 0's.
// MockTex2D_Sized: SizeX=SizeY=8, MipCount=4.
// Mip k has dimensions 8>>k * 8>>k.
[Test]
[WarpSize(2, 1)]
void MockResource_GetDimensions_VaryingMip([MockResource(MockTex2D_Sized)] Texture2D<float4> tex)
{
    uint tid = WaveGetLaneIndex();  // 0 or 1
    uint w, h, levels;
    tex.GetDimensions(tid, w, h, levels);  // thread 0: mip 0 (8x8), thread 1: mip 1 (4x4)
    uint expectedW = 8u >> tid;
    ASSERT(w == expectedW);
    ASSERT(h == expectedW);
}

// --- MOCK_RESOURCE inline binding ---

RWTexture2D<float4> g_tex;

[Test]
void MockResource_Inline_Load()
{
    MOCK_RESOURCE(g_tex, MockTex2D);
    // After MockTex2D.Initialize(), pixel (2,1) has .x == 6.
    float4 val = g_tex.Load(int2(2, 1));
    ASSERT(val.x == 6.0);
    ASSERT(val.w == 1.0);
}

[Test]
void MockResource_Inline_Store()
{
    MOCK_RESOURCE(g_tex, MockTex2D);
    g_tex[int2(3, 0)] = float4(77, 0, 0, 1);
    float4 val = g_tex.Load(int2(3, 0));
    ASSERT(val.x == 77.0);
}

[Test]
void MockResource_Inline_Dimensions()
{
    MOCK_RESOURCE(g_tex, MockTex2D_Sized);
    uint w, h;
    g_tex.GetDimensions(w, h);
    ASSERT(w == 8);
    ASSERT(h == 8);
}

// ============================================================
// Legacy combined-sampler tex* intrinsics
//
// For a size-N texture with point filtering:
//   texelPos = UV * N - 0.5
// UV values chosen so texelPos lands exactly on a texel centre.
//
// 4-texel 1D layout:  texel[x].x = x  →  [0, 1, 2, 3]
// 4x4 2D layout:      texel[x,y].x = y*4+x
// 2x2x2 3D layout:    texel[x,y,z].x = z*4+y*2+x
// Cube layout:        returns float4(face, 0, 0, 1)  (z arg encodes face)
//
// Mocks must expose SizeX/SizeY/SizeZ so the sampler code knows the texture
// dimensions for UV-to-texel mapping.
// ============================================================

struct MockLegacy2D
{
    float4 data[16];

    int SizeX() { return 4; }
    int SizeY() { return 4; }
    int MipCount() { return 1; }

    void Initialize()
    {
        for (int i = 0; i < 16; i++)
            data[i] = float4(i, 0, 0, 1);
    }

    float4 Read(int x, int y, int z, int w, int mipLevel)
    {
        return data[y * 4 + x];
    }
};

struct MockLegacy1D
{
    int SizeX() { return 4; }
    int MipCount() { return 1; }
    void Initialize() {}
    float4 Read(int x, int y, int z, int w, int mipLevel) { return float4(float(x), 0, 0, 1); }
};

struct MockLegacy3D
{
    int SizeX() { return 2; }
    int SizeY() { return 2; }
    int SizeZ() { return 2; }
    int MipCount() { return 1; }
    void Initialize() {}
    float4 Read(int x, int y, int z, int w, int mipLevel) { return float4(float(z * 4 + y * 2 + x), 0, 0, 1); }
};

struct MockLegacyCube
{
    int SizeX() { return 2; }
    int SizeY() { return 2; }
    int MipCount() { return 1; }
    void Initialize() {}
    // z encodes face index (0=+X,1=-X,2=+Y,3=-Y,4=+Z,5=-Z)
    float4 Read(int x, int y, int z, int w, int mipLevel) { return float4(float(z), 0, 0, 1); }
};

sampler1D  g_legacySampler1D;
sampler2D  g_legacySampler2D;
sampler3D  g_legacySampler3D;
samplerCUBE g_legacySamplerCube;

// tex2Dlod: explicit lod=0, UV=(0.625,0.625) -> texelPos=(2,2) -> pixel(2,2).x=10
[Test]
void LegacySampler_tex2Dlod()
{
    MOCK_RESOURCE(g_legacySampler2D, MockLegacy2D);
    float4 val = tex2Dlod(g_legacySampler2D, float4(0.625, 0.625, 0, 0));
    ASSERT(val.x == 10.0);
}

// tex2D (2-arg): uniform UV -> derivatives=0 -> lod=0; UV=(0.625,0.375) -> pixel(2,1).x=6
[Test]
void LegacySampler_tex2D()
{
    MOCK_RESOURCE(g_legacySampler2D, MockLegacy2D);
    float4 val = tex2D(g_legacySampler2D, float2(0.625, 0.375));
    ASSERT(val.x == 6.0);
}

// tex2Dbias: bias=0 -> same lod as tex2D; UV=(0.625,0.375) -> pixel(2,1).x=6
[Test]
void LegacySampler_tex2Dbias()
{
    MOCK_RESOURCE(g_legacySampler2D, MockLegacy2D);
    float4 val = tex2Dbias(g_legacySampler2D, float4(0.625, 0.375, 0, 0.0));
    ASSERT(val.x == 6.0);
}

// tex2Dgrad: explicit ddx=ddy=0 -> lod=0; UV=(0.625,0.375) -> pixel(2,1).x=6
[Test]
void LegacySampler_tex2Dgrad()
{
    MOCK_RESOURCE(g_legacySampler2D, MockLegacy2D);
    float4 val = tex2Dgrad(g_legacySampler2D, float2(0.625, 0.375), float2(0, 0), float2(0, 0));
    ASSERT(val.x == 6.0);
}

// tex2Dproj: (u/w, v/w) = (1.25/2, 0.75/2) = (0.625, 0.375) -> pixel(2,1).x=6
[Test]
void LegacySampler_tex2Dproj()
{
    MOCK_RESOURCE(g_legacySampler2D, MockLegacy2D);
    float4 val = tex2Dproj(g_legacySampler2D, float4(1.25, 0.75, 0, 2.0));
    ASSERT(val.x == 6.0);
}

// tex1Dlod: UV=0.625 -> texelPos=2.0 -> texel[2].x=2
[Test]
void LegacySampler_tex1Dlod()
{
    MOCK_RESOURCE(g_legacySampler1D, MockLegacy1D);
    float4 val = tex1Dlod(g_legacySampler1D, float4(0.625, 0, 0, 0));
    ASSERT(val.x == 2.0);
}

// tex1D (2-arg): uniform UV -> lod=0; UV=0.375 -> texelPos=1.0 -> texel[1].x=1
[Test]
void LegacySampler_tex1D()
{
    MOCK_RESOURCE(g_legacySampler1D, MockLegacy1D);
    float4 val = tex1D(g_legacySampler1D, 0.375);
    ASSERT(val.x == 1.0);
}

// tex3Dlod: UV=(0.75,0.25,0.75) -> texelPos=(1,0,1) -> texel(1,0,1).x=5
[Test]
void LegacySampler_tex3Dlod()
{
    MOCK_RESOURCE(g_legacySampler3D, MockLegacy3D);
    float4 val = tex3Dlod(g_legacySampler3D, float4(0.75, 0.25, 0.75, 0));
    ASSERT(val.x == 5.0);
}

// texCUBElod: direction (1,0,0) -> face 0 (+X) -> Read returns float4(0,0,0,1)
[Test]
void LegacySampler_texCUBElod()
{
    MOCK_RESOURCE(g_legacySamplerCube, MockLegacyCube);
    float4 val = texCUBElod(g_legacySamplerCube, float4(1, 0, 0, 0));
    ASSERT(val.x == 0.0);
}

// texCUBElod: direction (0,1,0) -> face 2 (+Y) -> Read returns float4(2,0,0,1)
[Test]
void LegacySampler_texCUBElod_FaceY()
{
    MOCK_RESOURCE(g_legacySamplerCube, MockLegacyCube);
    float4 val = texCUBElod(g_legacySamplerCube, float4(0, 1, 0, 0));
    ASSERT(val.x == 2.0);
}
