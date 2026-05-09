// Name: Texture sampling example
// RenderMode: Pixel
// FragEntry: frag
// Texture: _TexA tex0.jpg
// Texture: _TexB tex5.jpg

// Input textures are configurable via the
// "Textures" window in the hamburger menu.

Texture2D _TexA;
Texture2D _TexB;
SamplerState _Samp;

float4 frag(float4 position : SV_Position) : SV_Target
{
    float2 uv = position.xy / _Resolution;
    float3 a = _TexA.SampleLevel(_Samp, uv, 0).rgb;
    float3 b = _TexB.SampleLevel(_Samp, uv, 0).rgb;
    float w = 0.5 + 0.5 * sin(_Time);
    return float4(lerp(a, b, w), 1);
}
