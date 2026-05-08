// Name: Raymarching basic
// RenderMode: Pixel
// FragEntry: frag

// Shader: https://www.shadertoy.com/view/Ml2XRD
// License: CC-BY-NC-SA-3.0
// Author: gyabo

#define glsl_mod(x, y) ((x) - (y) * floor((x) / (y)))

float map(float3 p)
{
    float3 n = float3(0, 1, 0);
    float k1 = 1.9;
    float k2 = (sin(p.x * k1) + sin(p.z * k1)) * 0.8;
    float k3 = (sin(p.y * k1) + sin(p.z * k1)) * 0.8;
    float w1 = 4.0 - dot(abs(p), normalize(n)) + k2;
    float w2 = 4.0 - dot(abs(p), normalize(n.yzx)) + k3;
    float s1 = length(glsl_mod(p.xy + float2(sin((p.z + p.x) * 2.0) * 0.3, cos(p.z + p.x) * 0.5), 2.0) - 1.0) - 0.2;
    float s2 = length(glsl_mod(0.5 + p.yz + float2(sin((p.z + p.x) * 2.0) * 0.3, cos(p.z + p.x) * 0.3), 2.0) - 1.0) - 0.2;
    return min(w1, min(w2, min(s1, s2)));
}

float4 frag(float4 position : SV_Position) : SV_Target
{
    position.y = _Resolution.y - position.y;
    float2 uv = position.xy / _Resolution * 2.0 - 1.0;
	uv.x *= _Resolution.x / _Resolution.y;
    float3 dir = normalize(float3(uv, 1.0));
    float3 pos = float3(0, 0, _Time);

    float t = 0.0;
    float tt = 0.0;
    for (int i = 0; i < 100; i++)
    {
        tt = map(pos + dir * t);
        if (tt < 0.001) break;
        t += tt * 0.45;
    }
    float3 ip = pos + dir * t;
    float3 col = sqrt(t * 0.1);
    return float4(0.05 * t + abs(dir) * col + max(0.0, map(ip - 0.1) - tt), 1);
}
