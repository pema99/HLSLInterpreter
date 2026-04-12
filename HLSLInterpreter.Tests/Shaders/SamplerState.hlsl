
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
