
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
