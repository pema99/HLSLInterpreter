// Name: Rainbow spiral
// RenderMode: Pixel
// FragEntry: frag

static float TWIST = 12.0;
static float PERIOD = 32.0;
static float ARMS = 3.0;

float3 hueShift(float3 color, float shift)
{
    float3 axis = 0.55735;
    float3 grayscale = dot(0.55735, color);
    float3 radial = color - axis;
    float3 tangent = cross(axis, radial);
    return radial * cos(shift * 6.2832) +
        tangent * sin(shift * 6.2832) + axis;
}

float4 frag(float4 position : SV_Position) : SV_Target
{
    float2 uv = (position.xy / _Resolution) - 0.5;
    uv.x *= _Resolution.x / _Resolution.y;
    float dist = length(uv) + _Time * 0.2;
    float angle = atan2(uv.y, uv.x) + 0.4;
    float wave = sin(dist * TWIST - angle * ARMS);
    wave = wave * 0.5 + 0.5;
    if (wave < 0.5)
    {
        float3 base = float3(1.0, 0.3, 0.1);
        float3 color = hueShift(base, wave + dist);
        return float4(color, 1);
    }
    else
    {
        float periodic = sin(dist * PERIOD);
        float inverse = cos(dist * PERIOD);
        return float4(periodic, 0, inverse, 1);
    }
}
