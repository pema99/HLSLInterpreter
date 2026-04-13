
// ResourceMethods.hlsl
// One test per method family per resource type, using [MockResource].

// ============================================================
//  Shared mock backing structs
// ============================================================

struct Mock1D
{
    float4 data[4];
    void Initialize() { for (int i = 0; i < 4; i++) data[i] = float4(i, 0, 0, 1); }
    int SizeX()    { return 4; }
    int MipCount() { return 2; }
    float4 Read(int x, int y, int z, int w, int mip)               { return data[clamp(x, 0, 3)]; }
    void  Write(int x, int y, int z, int w, int mip, float4 v)     { data[clamp(x, 0, 3)] = v; }
};

// Uniform (all red) — used for Sample/SampleBias/SampleGrad so interpolation always returns 1.
struct Mock1DUniform
{
    void Initialize() {}
    int SizeX()    { return 4; }
    int MipCount() { return 2; }
    float4 Read(int x, int y, int z, int w, int mip) { return float4(1, 0, 0, 1); }
};

// Depth texture (all 0.5) — used for SampleCmp* tests.
struct Mock1DDepth
{
    void Initialize() {}
    int SizeX()    { return 4; }
    int MipCount() { return 1; }
    float4 Read(int x, int y, int z, int w, int mip) { return float4(0.5, 0, 0, 1); }
};

// 1D array: 4 texels × 2 slices.  y == array slice in Read (interpreter convention).
struct Mock1DArray
{
    float4 data[8]; // [slice*4 + x]
    void Initialize() { for (int i = 0; i < 8; i++) data[i] = float4(i, 0, 0, 1); }
    int SizeX()    { return 4; }
    int SizeY()    { return 2; }  // array count
    int MipCount() { return 1; }
    float4 Read(int x, int y, int z, int w, int mip)           { return data[y * 4 + x]; }  // y = slice
    void  Write(int x, int y, int z, int w, int mip, float4 v) { data[y * 4 + x] = v; }
};

// 2D uniform (all red).
struct Mock2DUniform
{
    void Initialize() {}
    int SizeX()    { return 4; }
    int SizeY()    { return 4; }
    int MipCount() { return 2; }
    float4 Read(int x, int y, int z, int w, int mip) { return float4(1, 0, 0, 1); }
};

// 2D depth (all 0.5) — for SampleCmp* / GatherCmp* tests.
struct Mock2DDepth
{
    void Initialize() {}
    int SizeX()    { return 4; }
    int SizeY()    { return 4; }
    int MipCount() { return 1; }
    float4 Read(int x, int y, int z, int w, int mip) { return float4(0.5, 0, 0, 1); }
};

// 2D with per-texel index values — for Gather tests.
// After Initialize, pixel (x,y) has .x == y*4+x.
struct Mock2DGather
{
    float4 data[16];
    void Initialize() { for (int i = 0; i < 16; i++) data[i] = float4(i, 0, 0, 1); }
    int SizeX()    { return 4; }
    int SizeY()    { return 4; }
    int MipCount() { return 1; }
    float4 Read(int x, int y, int z, int w, int mip)
    {
        return data[clamp(y, 0, 3) * 4 + clamp(x, 0, 3)];
    }
};

// 2D MS: 4x4 — z/mip ignored, (x,y) is all that matters.
struct Mock2DMS
{
    float4 data[16];
    void Initialize() { for (int i = 0; i < 16; i++) data[i] = float4(i, 0, 0, 1); }
    int SizeX() { return 4; }
    int SizeY() { return 4; }
    float4 Read(int x, int y, int z, int w, int mip) { return data[y * 4 + x]; }
};

// 2D array: 4x4 x 2 slices.  z == array slice in Read.
struct Mock2DArray
{
    float4 data[32]; // [slice*16 + y*4 + x]
    void Initialize() { for (int i = 0; i < 32; i++) data[i] = float4(i % 16, 0, 0, 1); }
    int SizeX()    { return 4; }
    int SizeY()    { return 4; }
    int SizeZ()    { return 2; }  // array count
    int MipCount() { return 1; }
    float4 Read(int x, int y, int z, int w, int mip)           { return data[z * 16 + y * 4 + x]; }
    void  Write(int x, int y, int z, int w, int mip, float4 v) { data[z * 16 + y * 4 + x] = v; }
};

// 2D MS array: 4x4 x 2 slices — no mip.
struct Mock2DMSArray
{
    float4 data[32];
    void Initialize() { for (int i = 0; i < 32; i++) data[i] = float4(i % 16, 0, 0, 1); }
    int SizeX() { return 4; }
    int SizeY() { return 4; }
    int SizeZ() { return 2; }
    float4 Read(int x, int y, int z, int w, int mip) { return data[z * 16 + y * 4 + x]; }
};

// 3D texture: 4x4x4.
struct Mock3D
{
    float4 data[64];
    void Initialize() { for (int i = 0; i < 64; i++) data[i] = float4(i, 0, 0, 1); }
    int SizeX()    { return 4; }
    int SizeY()    { return 4; }
    int SizeZ()    { return 4; }
    int MipCount() { return 2; }
    float4 Read(int x, int y, int z, int w, int mip)           { return data[z * 16 + y * 4 + x]; }
    void  Write(int x, int y, int z, int w, int mip, float4 v) { data[z * 16 + y * 4 + x] = v; }
};

// 3D uniform.
struct Mock3DUniform
{
    void Initialize() {}
    int SizeX()    { return 4; }
    int SizeY()    { return 4; }
    int SizeZ()    { return 4; }
    int MipCount() { return 2; }
    float4 Read(int x, int y, int z, int w, int mip) { return float4(1, 0, 0, 1); }
};

// Cube texture: 1x1 faces, all red.  z == face index.
struct MockCube
{
    void Initialize() {}
    int SizeX()    { return 1; }
    int SizeY()    { return 1; }
    int MipCount() { return 1; }
    float4 Read(int x, int y, int z, int w, int mip) { return float4(1, 0, 0, 1); }
};

// Cube array: 1x1 faces, 1 element, all green.
struct MockCubeArray
{
    void Initialize() {}
    int SizeX()    { return 1; }
    int SizeY()    { return 1; }
    int SizeZ()    { return 1; }  // array count
    int MipCount() { return 1; }
    float4 Read(int x, int y, int z, int w, int mip) { return float4(0, 1, 0, 1); }
};

// Buffer / RWBuffer: 8 elements.
struct MockBuf
{
    float4 data[8];
    void Initialize() { for (int i = 0; i < 8; i++) data[i] = float4(i, 0, 0, 1); }
    int SizeX() { return 8; }
    float4 Read(int x, int y, int z, int w, int mip)           { return data[clamp(x, 0, 7)]; }
    void  Write(int x, int y, int z, int w, int mip, float4 v) { data[clamp(x, 0, 7)] = v; }
};

// ByteAddressBuffer: 8 uints = 32 bytes.  data[i] = i+1.
// Read receives x = byte offset; returns as float4 so .x component is the uint.
struct MockBAB
{
    uint data[8];
    void Initialize() { for (int i = 0; i < 8; i++) data[i] = (uint)(i + 1); }
    int SizeX() { return 32; }  // byte capacity
    float4 Read(int x, int y, int z, int w, int mip)           { return float4(data[x / 4], 0, 0, 0); }
    void  Write(int x, int y, int z, int w, int mip, float4 v) { data[x / 4] = (uint)v.x; }
};

// StructuredBuffer / RWStructuredBuffer / Append / Consume: 4 float4 elements.
struct MockSB
{
    float4 data[4];
    void Initialize() { for (int i = 0; i < 4; i++) data[i] = float4(i * 10 + 1, 0, 0, 1); }
    int SizeX() { return 4; }
    float4 Read(int x, int y, int z, int w, int mip)           { return data[clamp(x, 0, 3)]; }
    void  Write(int x, int y, int z, int w, int mip, float4 v) { data[clamp(x, 0, 3)] = v; }
};

// ============================================================
//  Texture1D
// ============================================================

[Test]
void Tex1D_Load([MockResource(Mock1D)] Texture1D<float4> tex)
{
    // int2(x, mip) for non-RW Texture1D.Load
    float4 v = tex.Load(int2(2, 0));
    ASSERT(v.x == 2.0);
}

[Test]
void Tex1D_GetDimensions_NoMip([MockResource(Mock1D)] Texture1D<float4> tex)
{
    uint w;
    tex.GetDimensions(w);
    ASSERT(w == 4);
}

[Test]
void Tex1D_GetDimensions_Mip([MockResource(Mock1D)] Texture1D<float4> tex)
{
    uint w, levels;
    tex.GetDimensions(0, w, levels);
    ASSERT(w == 4);
    ASSERT(levels == 2);
}

[Test]
void Tex1D_Sample([MockResource(Mock1DUniform)] Texture1D<float4> tex)
{
    SamplerState s { Filter = MIN_MAG_MIP_POINT; AddressU = Clamp; };
    float4 v = tex.Sample(s, 0.5);
    ASSERT(v.x == 1.0);
}

[Test]
void Tex1D_SampleLevel([MockResource(Mock1D)] Texture1D<float4> tex)
{
    SamplerState s { Filter = MIN_MAG_MIP_POINT; AddressU = Clamp; };
    // UV 0.625 = texel 2 center in 4-texel texture: texelPos = 0.625*4 - 0.5 = 2.0 → data[2].x = 2
    float4 v = tex.SampleLevel(s, 0.625, 0);
    ASSERT(v.x == 2.0);
}

[Test]
void Tex1D_SampleBias([MockResource(Mock1DUniform)] Texture1D<float4> tex)
{
    SamplerState s { Filter = MIN_MAG_MIP_POINT; AddressU = Clamp; };
    float4 v = tex.SampleBias(s, 0.5, 0.0);
    ASSERT(v.x == 1.0);
}

[Test]
void Tex1D_SampleGrad([MockResource(Mock1DUniform)] Texture1D<float4> tex)
{
    SamplerState s { Filter = MIN_MAG_MIP_POINT; AddressU = Clamp; };
    float4 v = tex.SampleGrad(s, 0.5, 0.0, 0.0);
    ASSERT(v.x == 1.0);
}

[Test]
void Tex1D_SampleCmp([MockResource(Mock1DDepth)] Texture1D<float4> tex)
{
    // depth=0.5, cmpVal=0.6  →  0.5 <= 0.6  → passes → 1.0
    SamplerComparisonState cs { ComparisonFunc = LESS_EQUAL; };
    float v = tex.SampleCmp(cs, 0.5, 0.6);
    ASSERT(v == 1.0);
}

[Test]
void Tex1D_SampleCmpLevelZero([MockResource(Mock1DDepth)] Texture1D<float4> tex)
{
    SamplerComparisonState cs { ComparisonFunc = LESS_EQUAL; };
    float v = tex.SampleCmpLevelZero(cs, 0.5, 0.6);
    ASSERT(v == 1.0);
}

[Test]
void Tex1D_CalculateLevelOfDetail([MockResource(Mock1DUniform)] Texture1D<float4> tex)
{
    SamplerState s { Filter = MIN_MAG_MIP_LINEAR; };
    // Uniform UV → zero gradient → LOD clamped to 0.
    float lod = tex.CalculateLevelOfDetail(s, 0.5);
    ASSERT(lod >= 0.0);
}

[Test]
void Tex1D_CalculateLevelOfDetailUnclamped([MockResource(Mock1DUniform)] Texture1D<float4> tex)
{
    SamplerState s { Filter = MIN_MAG_MIP_LINEAR; };
    float lod = tex.CalculateLevelOfDetailUnclamped(s, 0.5);
    ASSERT(lod < 100.0);  // -inf for zero-gradient; just verify it's finite/negative
}

// ============================================================
//  Texture1DArray
// ============================================================

[Test]
void Tex1DArr_Load([MockResource(Mock1DArray)] Texture1DArray<float4> tex)
{
    // int3(x, arraySlice, mip) for Texture1DArray
    float4 v = tex.Load(int3(1, 0, 0));  // slice 0, x=1 → data[0*4+1] = float4(1,0,0,1)
    ASSERT(v.x == 1.0);
}

[Test]
void Tex1DArr_GetDimensions_NoMip([MockResource(Mock1DArray)] Texture1DArray<float4> tex)
{
    uint w, elements;
    tex.GetDimensions(w, elements);
    ASSERT(w == 4);
    ASSERT(elements == 2);
}

[Test]
void Tex1DArr_GetDimensions_Mip([MockResource(Mock1DArray)] Texture1DArray<float4> tex)
{
    uint w, elements, levels;
    tex.GetDimensions(0, w, elements, levels);
    ASSERT(w == 4);
    ASSERT(elements == 2);
    ASSERT(levels == 1);
}

[Test]
void Tex1DArr_SampleLevel([MockResource(Mock1DArray)] Texture1DArray<float4> tex)
{
    SamplerState s { Filter = MIN_MAG_MIP_POINT; AddressU = Clamp; };
    // float2(u, arraySlice): slice 0, texel 1 center (u=0.375)
    float4 v = tex.SampleLevel(s, float2(0.375, 0), 0);
    ASSERT(v.x == 1.0);
}

// ============================================================
//  Texture2D  (Load / GetDimensions already covered in Resources.hlsl)
// ============================================================

[Test]
void Tex2D_SampleLevel([MockResource(Mock2DUniform)] Texture2D<float4> tex)
{
    SamplerState s { Filter = MIN_MAG_MIP_POINT; AddressU = Clamp; AddressV = Clamp; };
    float4 v = tex.SampleLevel(s, float2(0.5, 0.5), 0);
    ASSERT(v.x == 1.0);
}

[Test]
void Tex2D_Sample([MockResource(Mock2DUniform)] Texture2D<float4> tex)
{
    SamplerState s { Filter = MIN_MAG_MIP_POINT; AddressU = Clamp; AddressV = Clamp; };
    float4 v = tex.Sample(s, float2(0.5, 0.5));
    ASSERT(v.x == 1.0);
}

[Test]
void Tex2D_SampleBias([MockResource(Mock2DUniform)] Texture2D<float4> tex)
{
    SamplerState s { Filter = MIN_MAG_MIP_POINT; AddressU = Clamp; AddressV = Clamp; };
    float4 v = tex.SampleBias(s, float2(0.5, 0.5), 0.0);
    ASSERT(v.x == 1.0);
}

[Test]
void Tex2D_SampleGrad([MockResource(Mock2DUniform)] Texture2D<float4> tex)
{
    SamplerState s { Filter = MIN_MAG_MIP_POINT; AddressU = Clamp; AddressV = Clamp; };
    float4 v = tex.SampleGrad(s, float2(0.5, 0.5), float2(0, 0), float2(0, 0));
    ASSERT(v.x == 1.0);
}

[Test]
void Tex2D_SampleCmp([MockResource(Mock2DDepth)] Texture2D<float4> tex)
{
    // All depths 0.5; cmpVal 0.6 → 0.5 <= 0.6 → 1.0
    SamplerComparisonState cs { ComparisonFunc = LESS_EQUAL; };
    float v = tex.SampleCmp(cs, float2(0.5, 0.5), 0.6);
    ASSERT(v == 1.0);
}

[Test]
void Tex2D_SampleCmpLevel([MockResource(Mock2DDepth)] Texture2D<float4> tex)
{
    SamplerComparisonState cs { ComparisonFunc = LESS_EQUAL; };
    float v = tex.SampleCmpLevel(cs, float2(0.5, 0.5), 0.6, 0);
    ASSERT(v == 1.0);
}

[Test]
void Tex2D_SampleCmpLevelZero([MockResource(Mock2DDepth)] Texture2D<float4> tex)
{
    SamplerComparisonState cs { ComparisonFunc = LESS_EQUAL; };
    float v = tex.SampleCmpLevelZero(cs, float2(0.5, 0.5), 0.6);
    ASSERT(v == 1.0);
}

[Test]
void Tex2D_SampleCmpBias([MockResource(Mock2DDepth)] Texture2D<float4> tex)
{
    SamplerComparisonState cs { ComparisonFunc = LESS_EQUAL; };
    float v = tex.SampleCmpBias(cs, float2(0.5, 0.5), 0.6, 0.0);
    ASSERT(v == 1.0);
}

[Test]
void Tex2D_SampleCmpGrad([MockResource(Mock2DDepth)] Texture2D<float4> tex)
{
    SamplerComparisonState cs { ComparisonFunc = LESS_EQUAL; };
    float v = tex.SampleCmpGrad(cs, float2(0.5, 0.5), 0.6, float2(0, 0), float2(0, 0));
    ASSERT(v == 1.0);
}

[Test]
void Tex2D_CalculateLevelOfDetail([MockResource(Mock2DUniform)] Texture2D<float4> tex)
{
    SamplerState s { Filter = MIN_MAG_MIP_LINEAR; };
    float lod = tex.CalculateLevelOfDetail(s, float2(0.5, 0.5));
    ASSERT(lod >= 0.0);
}

[Test]
void Tex2D_CalculateLevelOfDetailUnclamped([MockResource(Mock2DUniform)] Texture2D<float4> tex)
{
    SamplerState s { Filter = MIN_MAG_MIP_LINEAR; };
    float lod = tex.CalculateLevelOfDetailUnclamped(s, float2(0.5, 0.5));
    ASSERT(lod < 100.0);
}

// Gather at UV (0.375, 0.375) on 4x4 texture:
//   baseX=floor(0.375*4-0.5)=1, baseY=1.
//   Footprint: (1,2),(2,2),(2,1),(1,1) → values 9,10,6,5.
//   Output order: .x=(u0,v1)=9, .y=(u1,v1)=10, .z=(u1,v0)=6, .w=(u0,v0)=5.
[Test]
void Tex2D_Gather([MockResource(Mock2DGather)] Texture2D<float4> tex)
{
    SamplerState s { Filter = MIN_MAG_MIP_POINT; AddressU = Clamp; AddressV = Clamp; };
    float4 g = tex.Gather(s, float2(0.375, 0.375));
    ASSERT(g.w == 5.0);
    ASSERT(g.z == 6.0);
    ASSERT(g.x == 9.0);
    ASSERT(g.y == 10.0);
}

[Test]
void Tex2D_GatherRed([MockResource(Mock2DGather)] Texture2D<float4> tex)
{
    SamplerState s { Filter = MIN_MAG_MIP_POINT; AddressU = Clamp; AddressV = Clamp; };
    float4 g = tex.GatherRed(s, float2(0.375, 0.375));
    ASSERT(g.w == 5.0);
}

[Test]
void Tex2D_GatherGreen([MockResource(Mock2DGather)] Texture2D<float4> tex)
{
    // All texels have .y == 0
    SamplerState s { Filter = MIN_MAG_MIP_POINT; AddressU = Clamp; AddressV = Clamp; };
    float4 g = tex.GatherGreen(s, float2(0.375, 0.375));
    ASSERT(g.x == 0.0);
}

[Test]
void Tex2D_GatherBlue([MockResource(Mock2DGather)] Texture2D<float4> tex)
{
    SamplerState s { Filter = MIN_MAG_MIP_POINT; AddressU = Clamp; AddressV = Clamp; };
    float4 g = tex.GatherBlue(s, float2(0.375, 0.375));
    ASSERT(g.x == 0.0);
}

[Test]
void Tex2D_GatherAlpha([MockResource(Mock2DGather)] Texture2D<float4> tex)
{
    // All texels have .w == 1
    SamplerState s { Filter = MIN_MAG_MIP_POINT; AddressU = Clamp; AddressV = Clamp; };
    float4 g = tex.GatherAlpha(s, float2(0.375, 0.375));
    ASSERT(g.x == 1.0);
}

[Test]
void Tex2D_GatherCmp([MockResource(Mock2DDepth)] Texture2D<float4> tex)
{
    // All depths 0.5, cmpVal 0.6 → all pass → float4(1,1,1,1)
    SamplerComparisonState cs { ComparisonFunc = LESS_EQUAL; };
    float4 g = tex.GatherCmp(cs, float2(0.375, 0.375), 0.6);
    ASSERT(g.x == 1.0);
    ASSERT(g.w == 1.0);
}

[Test]
void Tex2D_GatherCmpRed([MockResource(Mock2DDepth)] Texture2D<float4> tex)
{
    SamplerComparisonState cs { ComparisonFunc = LESS_EQUAL; };
    float4 g = tex.GatherCmpRed(cs, float2(0.375, 0.375), 0.6);
    ASSERT(g.x == 1.0);
}

[Test]
void Tex2D_GatherCmpGreen([MockResource(Mock2DDepth)] Texture2D<float4> tex)
{
    // .y == 0, 0 <= 0.6 → passes → 1.0
    SamplerComparisonState cs { ComparisonFunc = LESS_EQUAL; };
    float4 g = tex.GatherCmpGreen(cs, float2(0.375, 0.375), 0.6);
    ASSERT(g.x == 1.0);
}

[Test]
void Tex2D_GatherCmpBlue([MockResource(Mock2DDepth)] Texture2D<float4> tex)
{
    SamplerComparisonState cs { ComparisonFunc = LESS_EQUAL; };
    float4 g = tex.GatherCmpBlue(cs, float2(0.375, 0.375), 0.6);
    ASSERT(g.x == 1.0);
}

[Test]
void Tex2D_GatherCmpAlpha([MockResource(Mock2DDepth)] Texture2D<float4> tex)
{
    // .w == 1, 1 <= 0.6 → fails → 0.0
    SamplerComparisonState cs { ComparisonFunc = LESS_EQUAL; };
    float4 g = tex.GatherCmpAlpha(cs, float2(0.375, 0.375), 0.6);
    ASSERT(g.x == 0.0);
}

// ============================================================
//  Texture2DMS
// ============================================================

[Test]
void Tex2DMS_Load([MockResource(Mock2DMS)] Texture2DMS<float4> tex)
{
    // Load(int2 Location, int SampleIndex) — sample index ignored in mock
    float4 v = tex.Load(int2(2, 1), 0);
    ASSERT(v.x == 6.0);  // data[1*4+2] = 6
}

[Test]
void Tex2DMS_GetDimensions([MockResource(Mock2DMS)] Texture2DMS<float4> tex)
{
    uint w, h, samples;
    tex.GetDimensions(w, h, samples);
    ASSERT(w == 4);
    ASSERT(h == 4);
    ASSERT(samples == 1);
}

[Test]
void Tex2DMS_GetSamplePosition([MockResource(Mock2DMS)] Texture2DMS<float4> tex)
{
    float2 pos = tex.GetSamplePosition(0);
    // Implementation always returns (0.5, 0.5)
    ASSERT(pos.x == 0.5);
    ASSERT(pos.y == 0.5);
}

// ============================================================
//  Texture2DArray
// ============================================================

[Test]
void Tex2DArr_Load([MockResource(Mock2DArray)] Texture2DArray<float4> tex)
{
    // int4(x, y, arraySlice, mip) for Texture2DArray
    float4 v = tex.Load(int4(1, 2, 0, 0));  // slice 0, (1,2) → data[0*16+2*4+1]=data[9]=9
    ASSERT(v.x == 9.0);
}

[Test]
void Tex2DArr_GetDimensions_NoMip([MockResource(Mock2DArray)] Texture2DArray<float4> tex)
{
    uint w, h, elements;
    tex.GetDimensions(w, h, elements);
    ASSERT(w == 4);
    ASSERT(h == 4);
    ASSERT(elements == 2);
}

[Test]
void Tex2DArr_GetDimensions_Mip([MockResource(Mock2DArray)] Texture2DArray<float4> tex)
{
    uint w, h, elements, levels;
    tex.GetDimensions(0, w, h, elements, levels);
    ASSERT(w == 4);
    ASSERT(h == 4);
    ASSERT(elements == 2);
    ASSERT(levels == 1);
}

[Test]
void Tex2DArr_SampleLevel([MockResource(Mock2DArray)] Texture2DArray<float4> tex)
{
    SamplerState s { Filter = MIN_MAG_MIP_POINT; AddressU = Clamp; AddressV = Clamp; };
    // float3(u, v, arraySlice) — slice 0, texel (0,0) center
    float4 v = tex.SampleLevel(s, float3(0.125, 0.125, 0), 0);
    ASSERT(v.x == 0.0);  // data[0] = float4(0,0,0,1)
}

[Test]
void Tex2DArr_Gather([MockResource(Mock2DArray)] Texture2DArray<float4> tex)
{
    SamplerState s { Filter = MIN_MAG_MIP_POINT; AddressU = Clamp; AddressV = Clamp; };
    // float3(u, v, arraySlice): slice 0, same footprint as Tex2D_Gather
    float4 g = tex.Gather(s, float3(0.375, 0.375, 0));
    ASSERT(g.w == 5.0);
}

[Test]
void Tex2DArr_GatherCmp([MockResource(Mock2DDepth)] Texture2DArray<float4> tex)
{
    SamplerComparisonState cs { ComparisonFunc = LESS_EQUAL; };
    float4 g = tex.GatherCmp(cs, float3(0.375, 0.375, 0), 0.6);
    ASSERT(g.x == 1.0);
}

// ============================================================
//  Texture2DMSArray
// ============================================================

[Test]
void Tex2DMSArr_Load([MockResource(Mock2DMSArray)] Texture2DMSArray<float4> tex)
{
    // Load(int3 Location, int SampleIndex): Location = int3(x, y, arraySlice)
    float4 v = tex.Load(int3(1, 2, 0), 0);  // slice 0, (1,2) → data[9] = 9
    ASSERT(v.x == 9.0);
}

[Test]
void Tex2DMSArr_GetDimensions([MockResource(Mock2DMSArray)] Texture2DMSArray<float4> tex)
{
    uint w, h, elements, samples;
    tex.GetDimensions(w, h, elements, samples);
    ASSERT(w == 4);
    ASSERT(h == 4);
    ASSERT(elements == 2);
    ASSERT(samples == 1);
}

[Test]
void Tex2DMSArr_GetSamplePosition([MockResource(Mock2DMSArray)] Texture2DMSArray<float4> tex)
{
    float2 pos = tex.GetSamplePosition(0);
    ASSERT(pos.x == 0.5);
}

// ============================================================
//  Texture3D
// ============================================================

[Test]
void Tex3D_Load([MockResource(Mock3D)] Texture3D<float4> tex)
{
    // int4(x, y, z, mip) for Texture3D
    float4 v = tex.Load(int4(1, 2, 3, 0));  // data[3*16+2*4+1] = data[57] = float4(57,0,0,1)
    ASSERT(v.x == 57.0);
}

[Test]
void Tex3D_GetDimensions_NoMip([MockResource(Mock3D)] Texture3D<float4> tex)
{
    uint w, h, d;
    tex.GetDimensions(w, h, d);
    ASSERT(w == 4);
    ASSERT(h == 4);
    ASSERT(d == 4);
}

[Test]
void Tex3D_GetDimensions_Mip([MockResource(Mock3D)] Texture3D<float4> tex)
{
    uint w, h, d, levels;
    tex.GetDimensions(0, w, h, d, levels);
    ASSERT(w == 4);
    ASSERT(h == 4);
    ASSERT(d == 4);
    ASSERT(levels == 2);
}

[Test]
void Tex3D_SampleLevel([MockResource(Mock3DUniform)] Texture3D<float4> tex)
{
    SamplerState s { Filter = MIN_MAG_MIP_POINT; AddressU = Clamp; AddressV = Clamp; AddressW = Clamp; };
    float4 v = tex.SampleLevel(s, float3(0.5, 0.5, 0.5), 0);
    ASSERT(v.x == 1.0);
}

[Test]
void Tex3D_CalculateLevelOfDetail([MockResource(Mock3DUniform)] Texture3D<float4> tex)
{
    SamplerState s { Filter = MIN_MAG_MIP_LINEAR; };
    float lod = tex.CalculateLevelOfDetail(s, float3(0.5, 0.5, 0.5));
    ASSERT(lod >= 0.0);
}

// ============================================================
//  TextureCube
// ============================================================

[Test]
void TexCube_GetDimensions_NoMip([MockResource(MockCube)] TextureCube<float4> tex)
{
    uint w, h;
    tex.GetDimensions(w, h);
    ASSERT(w == 1);
    ASSERT(h == 1);
}

[Test]
void TexCube_GetDimensions_Mip([MockResource(MockCube)] TextureCube<float4> tex)
{
    uint w, h, levels;
    tex.GetDimensions(0, w, h, levels);
    ASSERT(w == 1);
    ASSERT(levels == 1);
}

[Test]
void TexCube_SampleLevel([MockResource(MockCube)] TextureCube<float4> tex)
{
    SamplerState s { Filter = MIN_MAG_MIP_POINT; };
    // Direction toward +Z face (face 4) — any direction works since all faces are red
    float4 v = tex.SampleLevel(s, float3(0, 0, 1), 0);
    ASSERT(v.x == 1.0);
}

[Test]
void TexCube_Sample([MockResource(MockCube)] TextureCube<float4> tex)
{
    SamplerState s { Filter = MIN_MAG_MIP_POINT; };
    float4 v = tex.Sample(s, float3(0, 0, 1));
    ASSERT(v.x == 1.0);
}

[Test]
void TexCube_CalculateLevelOfDetail([MockResource(MockCube)] TextureCube<float4> tex)
{
    SamplerState s { Filter = MIN_MAG_MIP_LINEAR; };
    float lod = tex.CalculateLevelOfDetail(s, float3(0, 0, 1));
    ASSERT(lod >= 0.0);
}

// ============================================================
//  TextureCubeArray
// ============================================================

[Test]
void TexCubeArr_GetDimensions_NoMip([MockResource(MockCubeArray)] TextureCubeArray<float4> tex)
{
    uint w, h, elements;
    tex.GetDimensions(w, h, elements);
    ASSERT(w == 1);
    ASSERT(elements == 1);
}

[Test]
void TexCubeArr_GetDimensions_Mip([MockResource(MockCubeArray)] TextureCubeArray<float4> tex)
{
    uint w, h, elements, levels;
    tex.GetDimensions(0, w, h, elements, levels);
    ASSERT(w == 1);
    ASSERT(elements == 1);
    ASSERT(levels == 1);
}

[Test]
void TexCubeArr_SampleLevel([MockResource(MockCubeArray)] TextureCubeArray<float4> tex)
{
    SamplerState s { Filter = MIN_MAG_MIP_POINT; };
    // float4(dir.xyz, arraySlice): slice 0, direction +Z
    float4 v = tex.SampleLevel(s, float4(0, 0, 1, 0), 0);
    ASSERT(v.y == 1.0);  // MockCubeArray returns float4(0,1,0,1)
}

// ============================================================
//  RWTexture1D
// ============================================================

[Test]
void RWTex1D_Load([MockResource(Mock1D)] RWTexture1D<float4> tex)
{
    // RW Load takes a scalar int (no mip component)
    float4 v = tex.Load(3);
    ASSERT(v.x == 3.0);
}

[Test]
void RWTex1D_GetDimensions([MockResource(Mock1D)] RWTexture1D<float4> tex)
{
    uint w;
    tex.GetDimensions(w);
    ASSERT(w == 4);
}

[Test]
void RWTex1D_SubscriptReadWrite([MockResource(Mock1D)] RWTexture1D<float4> tex)
{
    tex[2] = float4(99, 0, 0, 1);
    float4 v = tex.Load(2);
    ASSERT(v.x == 99.0);
}

// ============================================================
//  RWTexture1DArray
// ============================================================

[Test]
void RWTex1DArr_Load([MockResource(Mock1DArray)] RWTexture1DArray<float4> tex)
{
    // int2(x, arraySlice) for RWTexture1DArray (no mip)
    float4 v = tex.Load(int2(1, 0));  // slice 0, x=1 → data[1] = float4(1,0,0,1)
    ASSERT(v.x == 1.0);
}

[Test]
void RWTex1DArr_GetDimensions([MockResource(Mock1DArray)] RWTexture1DArray<float4> tex)
{
    uint w, elements;
    tex.GetDimensions(w, elements);
    ASSERT(w == 4);
    ASSERT(elements == 2);
}

[Test]
void RWTex1DArr_SubscriptReadWrite([MockResource(Mock1DArray)] RWTexture1DArray<float4> tex)
{
    tex[int2(0, 1)] = float4(77, 0, 0, 1);  // x=0, slice=1
    float4 v = tex.Load(int2(0, 1));
    ASSERT(v.x == 77.0);
}

// ============================================================
//  RWTexture2DArray
// ============================================================

[Test]
void RWTex2DArr_Load([MockResource(Mock2DArray)] RWTexture2DArray<float4> tex)
{
    // int3(x, y, arraySlice) for RWTexture2DArray
    float4 v = tex.Load(int3(1, 2, 0));  // same as Tex2DArr_Load: data[9] = 9
    ASSERT(v.x == 9.0);
}

[Test]
void RWTex2DArr_GetDimensions([MockResource(Mock2DArray)] RWTexture2DArray<float4> tex)
{
    uint w, h, elements;
    tex.GetDimensions(w, h, elements);
    ASSERT(w == 4);
    ASSERT(h == 4);
    ASSERT(elements == 2);
}

[Test]
void RWTex2DArr_SubscriptReadWrite([MockResource(Mock2DArray)] RWTexture2DArray<float4> tex)
{
    tex[int3(0, 0, 1)] = float4(55, 0, 0, 1);  // slice 1
    float4 v = tex.Load(int3(0, 0, 1));
    ASSERT(v.x == 55.0);
}

// ============================================================
//  RWTexture3D
// ============================================================

[Test]
void RWTex3D_Load([MockResource(Mock3D)] RWTexture3D<float4> tex)
{
    // int3(x, y, z) for RWTexture3D
    float4 v = tex.Load(int3(1, 2, 3));  // data[57] = float4(57,0,0,1)
    ASSERT(v.x == 57.0);
}

[Test]
void RWTex3D_GetDimensions([MockResource(Mock3D)] RWTexture3D<float4> tex)
{
    uint w, h, d;
    tex.GetDimensions(w, h, d);
    ASSERT(w == 4);
    ASSERT(h == 4);
    ASSERT(d == 4);
}

[Test]
void RWTex3D_SubscriptReadWrite([MockResource(Mock3D)] RWTexture3D<float4> tex)
{
    tex[int3(0, 0, 0)] = float4(33, 0, 0, 1);
    float4 v = tex.Load(int3(0, 0, 0));
    ASSERT(v.x == 33.0);
}

// ============================================================
//  Buffer<T>
// ============================================================

[Test]
void Buf_Load([MockResource(MockBuf)] Buffer<float4> buf)
{
    float4 v = buf.Load(3);
    ASSERT(v.x == 3.0);
}

[Test]
void Buf_GetDimensions([MockResource(MockBuf)] Buffer<float4> buf)
{
    uint w;
    buf.GetDimensions(w);
    ASSERT(w == 8);
}

// ============================================================
//  RWBuffer<T>
// ============================================================

[Test]
void RWBuf_Load([MockResource(MockBuf)] RWBuffer<float4> buf)
{
    float4 v = buf.Load(3);
    ASSERT(v.x == 3.0);
}

[Test]
void RWBuf_GetDimensions([MockResource(MockBuf)] RWBuffer<float4> buf)
{
    uint w;
    buf.GetDimensions(w);
    ASSERT(w == 8);
}

[Test]
void RWBuf_SubscriptReadWrite([MockResource(MockBuf)] RWBuffer<float4> buf)
{
    buf[2] = float4(88, 0, 0, 1);
    float4 v = buf.Load(2);
    ASSERT(v.x == 88.0);
}

// ============================================================
//  ByteAddressBuffer
// ============================================================

[Test]
void BAB_Load([MockResource(MockBAB)] ByteAddressBuffer buf)
{
    // Byte offset 0 → data[0] = 1
    uint v = buf.Load(0);
    ASSERT(v == 1);
}

[Test]
void BAB_Load2([MockResource(MockBAB)] ByteAddressBuffer buf)
{
    uint2 v = buf.Load2(0);  // data[0]=1, data[1]=2
    ASSERT(v.x == 1);
    ASSERT(v.y == 2);
}

[Test]
void BAB_Load3([MockResource(MockBAB)] ByteAddressBuffer buf)
{
    uint3 v = buf.Load3(0);
    ASSERT(v.x == 1);
    ASSERT(v.y == 2);
    ASSERT(v.z == 3);
}

[Test]
void BAB_Load4([MockResource(MockBAB)] ByteAddressBuffer buf)
{
    uint4 v = buf.Load4(0);
    ASSERT(v.x == 1);
    ASSERT(v.y == 2);
    ASSERT(v.z == 3);
    ASSERT(v.w == 4);
}

[Test]
void BAB_GetDimensions([MockResource(MockBAB)] ByteAddressBuffer buf)
{
    uint dim;
    buf.GetDimensions(dim);
    ASSERT(dim == 32);  // SizeX = 32 bytes
}

// ============================================================
//  RWByteAddressBuffer — Store / Load / Atomics
// ============================================================

[Test]
void RWBAB_Store([MockResource(MockBAB)] RWByteAddressBuffer buf)
{
    buf.Store(0, 42u);
    uint v = buf.Load(0);
    ASSERT(v == 42);
}

[Test]
void RWBAB_Store2([MockResource(MockBAB)] RWByteAddressBuffer buf)
{
    buf.Store2(0, uint2(10, 20));
    uint2 v = buf.Load2(0);
    ASSERT(v.x == 10);
    ASSERT(v.y == 20);
}

[Test]
void RWBAB_Store3([MockResource(MockBAB)] RWByteAddressBuffer buf)
{
    buf.Store3(0, uint3(10, 20, 30));
    uint3 v = buf.Load3(0);
    ASSERT(v.x == 10);
    ASSERT(v.z == 30);
}

[Test]
void RWBAB_Store4([MockResource(MockBAB)] RWByteAddressBuffer buf)
{
    buf.Store4(0, uint4(10, 20, 30, 40));
    uint4 v = buf.Load4(0);
    ASSERT(v.x == 10);
    ASSERT(v.w == 40);
}

[Test]
[WarpSize(1, 1)]
void RWBAB_InterlockedAdd([MockResource(MockBAB)] RWByteAddressBuffer buf)
{
    // data[0]=1; add 10 → new=11, orig=1
    uint orig;
    buf.InterlockedAdd(0, 10u, orig);
    ASSERT(orig == 1);
    ASSERT(buf.Load(0) == 11);
}

[Test]
[WarpSize(1, 1)]
void RWBAB_InterlockedMin([MockResource(MockBAB)] RWByteAddressBuffer buf)
{
    // data[1]=2; min(2,1)=1
    uint orig;
    buf.InterlockedMin(4, 1u, orig);
    ASSERT(orig == 2);
    ASSERT(buf.Load(4) == 1);
}

[Test]
[WarpSize(1, 1)]
void RWBAB_InterlockedMax([MockResource(MockBAB)] RWByteAddressBuffer buf)
{
    // data[1]=2; max(2,100)=100
    uint orig;
    buf.InterlockedMax(4, 100u, orig);
    ASSERT(orig == 2);
    ASSERT(buf.Load(4) == 100);
}

[Test]
[WarpSize(1, 1)]
void RWBAB_InterlockedAnd([MockResource(MockBAB)] RWByteAddressBuffer buf)
{
    // data[0]=1 = 0b0001; 1 & 0b1010 = 0
    uint orig;
    buf.InterlockedAnd(0, 0xFFFFFFFEu, orig);  // clear bit 0
    ASSERT(orig == 1);
    ASSERT(buf.Load(0) == 0);
}

[Test]
[WarpSize(1, 1)]
void RWBAB_InterlockedOr([MockResource(MockBAB)] RWByteAddressBuffer buf)
{
    // data[0]=1; 1 | 6 = 7
    uint orig;
    buf.InterlockedOr(0, 6u, orig);
    ASSERT(orig == 1);
    ASSERT(buf.Load(0) == 7);
}

[Test]
[WarpSize(1, 1)]
void RWBAB_InterlockedXor([MockResource(MockBAB)] RWByteAddressBuffer buf)
{
    // data[0]=1; 1 ^ 3 = 2
    uint orig;
    buf.InterlockedXor(0, 3u, orig);
    ASSERT(orig == 1);
    ASSERT(buf.Load(0) == 2);
}

[Test]
[WarpSize(1, 1)]
void RWBAB_InterlockedExchange([MockResource(MockBAB)] RWByteAddressBuffer buf)
{
    uint orig;
    buf.InterlockedExchange(0, 99u, orig);
    ASSERT(orig == 1);
    ASSERT(buf.Load(0) == 99);
}

[Test]
[WarpSize(1, 1)]
void RWBAB_InterlockedCompareExchange([MockResource(MockBAB)] RWByteAddressBuffer buf)
{
    // data[0]=1; compare=1 matches → store 42
    uint orig;
    buf.InterlockedCompareExchange(0, 1u, 42u, orig);
    ASSERT(orig == 1);
    ASSERT(buf.Load(0) == 42);
}

[Test]
[WarpSize(1, 1)]
void RWBAB_InterlockedCompareStore([MockResource(MockBAB)] RWByteAddressBuffer buf)
{
    // data[0]=1; compare=1 matches → store 77
    buf.InterlockedCompareStore(0, 1u, 77u);
    ASSERT(buf.Load(0) == 77);
}

[Test]
[WarpSize(1, 1)]
void RWBAB_InterlockedAdd64([MockResource(MockBAB)] RWByteAddressBuffer buf)
{
    // 64-bit at offset 0: lo=data[0]=1, hi=data[1]=2
    uint2 orig;
    buf.InterlockedAdd64(0, uint2(5, 0), orig);
    ASSERT(orig.x == 1);  // original lo
    ASSERT(orig.y == 2);  // original hi
    ASSERT(buf.Load(0) == 6);   // 1+5=6
    ASSERT(buf.Load(4) == 2);   // hi unchanged
}

[Test]
[WarpSize(1, 1)]
void RWBAB_InterlockedExchangeFloat([MockResource(MockBAB)] RWByteAddressBuffer buf)
{
    // Pre-store float bits of 1.0 at offset 0
    buf.Store(0, asuint(1.0));
    float orig;
    buf.InterlockedExchangeFloat(0, 2.0, orig);
    ASSERT(orig == 1.0);
    ASSERT(asfloat(buf.Load(0)) == 2.0);
}

[Test]
[WarpSize(1, 1)]
void RWBAB_InterlockedCompareExchangeFloatBitwise([MockResource(MockBAB)] RWByteAddressBuffer buf)
{
    buf.Store(0, asuint(3.0));
    float orig;
    buf.InterlockedCompareExchangeFloatBitwise(0, 3.0, 5.0, orig);
    ASSERT(orig == 3.0);
    ASSERT(asfloat(buf.Load(0)) == 5.0);
}

[Test]
[WarpSize(1, 1)]
void RWBAB_InterlockedCompareStoreFloatBitwise([MockResource(MockBAB)] RWByteAddressBuffer buf)
{
    buf.Store(0, asuint(7.0));
    buf.InterlockedCompareStoreFloatBitwise(0, 7.0, 9.0);
    ASSERT(asfloat(buf.Load(0)) == 9.0);
}

[Test]
void RWBAB_GetDimensions([MockResource(MockBAB)] RWByteAddressBuffer buf)
{
    uint dim;
    buf.GetDimensions(dim);
    ASSERT(dim == 32);
}

// ============================================================
//  StructuredBuffer<float4>
// ============================================================

[Test]
void SB_Load([MockResource(MockSB)] StructuredBuffer<float4> buf)
{
    float4 v = buf.Load(1);  // data[1] = float4(11,0,0,1)
    ASSERT(v.x == 11.0);
}

[Test]
void SB_GetDimensions([MockResource(MockSB)] StructuredBuffer<float4> buf)
{
    uint count, stride;
    buf.GetDimensions(count, stride);
    ASSERT(count == 4);
    ASSERT(stride == 16);  // sizeof(float4) = 16 bytes
}

// ============================================================
//  RWStructuredBuffer<float4>
// ============================================================

[Test]
void RWSB_Load([MockResource(MockSB)] RWStructuredBuffer<float4> buf)
{
    float4 v = buf.Load(2);  // data[2] = float4(21,0,0,1)
    ASSERT(v.x == 21.0);
}

[Test]
void RWSB_GetDimensions([MockResource(MockSB)] RWStructuredBuffer<float4> buf)
{
    uint count, stride;
    buf.GetDimensions(count, stride);
    ASSERT(count == 4);
    ASSERT(stride == 16);
}

[Test]
[WarpSize(1, 1)]
void RWSB_IncrementDecrementCounter([MockResource(MockSB)] RWStructuredBuffer<float4> buf)
{
    uint c0 = buf.IncrementCounter();  // 0, counter→1
    uint c1 = buf.IncrementCounter();  // 1, counter→2
    uint c2 = buf.DecrementCounter();  // 1 (pre-dec of 2), counter→1
    ASSERT(c0 == 0);
    ASSERT(c1 == 1);
    ASSERT(c2 == 1);
}

// ============================================================
//  AppendStructuredBuffer<float4>
// ============================================================

[Test]
void AppendSB_Append([MockResource(MockSB)] AppendStructuredBuffer<float4> buf)
{
    buf.Append(float4(42, 0, 0, 1));  // writes to index 0 (counter starts at 0)
    // Verify dimensions are still intact
    uint count, stride;
    buf.GetDimensions(count, stride);
    ASSERT(count == 4);
    ASSERT(stride == 16);
}

[Test]
void AppendSB_GetDimensions([MockResource(MockSB)] AppendStructuredBuffer<float4> buf)
{
    uint count, stride;
    buf.GetDimensions(count, stride);
    ASSERT(count == 4);
    ASSERT(stride == 16);
}

// ============================================================
//  ConsumeStructuredBuffer<float4>
// ============================================================

[Test]
[WarpSize(1, 1)]
void ConsumeSB_Consume([MockResource(MockSB)] ConsumeStructuredBuffer<float4> buf)
{
    // Counter starts at 0; Consume() reads index 0 (post-decrement).
    float4 v = buf.Consume();
    ASSERT(v.x == 1.0);  // data[0] = float4(1,0,0,1)
}

[Test]
void ConsumeSB_GetDimensions([MockResource(MockSB)] ConsumeStructuredBuffer<float4> buf)
{
    uint count, stride;
    buf.GetDimensions(count, stride);
    ASSERT(count == 4);
    ASSERT(stride == 16);
}
