// Name: Cyber fuji 2020
// RenderMode: Pixel
// FragEntry: frag

// Shader: https://www.shadertoy.com/view/Wt33Wf
// License: CC-BY-3.0
// Author: kaiware007

#define glsl_mod(x, y) ((x) - (y) * floor((x) / (y)))

float sun(float2 uv, float battery)
{
    float val = smoothstep(0.3, 0.29, length(uv));
    float bloom = smoothstep(0.7, 0.0, length(uv));
    float cut = 3.0 * sin((uv.y + _Time * 0.2 * (battery + 0.02)) * 100.0)
              + clamp(uv.y * 14.0 + 1.0, -6.0, 6.0);
    cut = clamp(cut, 0.0, 1.0);
    return clamp(val * cut, 0.0, 1.0) + bloom * 0.6;
}

float grid(float2 uv, float battery)
{
    float2 size = float2(uv.y, uv.y * uv.y * 0.2) * 0.01;
    uv += float2(0.0, _Time * 4.0 * (battery + 0.05));
    uv = abs(frac(uv) - 0.5);
    float2 lines = smoothstep(size, 0, uv);
    lines += smoothstep(size * 5.0, 0, uv) * 0.4 * battery;
    return clamp(lines.x + lines.y, 0.0, 3.0);
}

float dot2(float2 v) { return dot(v, v); }

float sdTrapezoid(float2 p, float r1, float r2, float he)
{
    float2 k1 = float2(r2, he);
    float2 k2 = float2(r2 - r1, 2.0 * he);
    p.x = abs(p.x);
    float2 ca = float2(p.x - min(p.x, p.y < 0.0 ? r1 : r2), abs(p.y) - he);
    float2 cb = p - k1 + k2 * clamp(dot(k1 - p, k2) / dot2(k2), 0.0, 1.0);
    float s = (cb.x < 0.0 && ca.y < 0.0) ? -1.0 : 1.0;
    return s * sqrt(min(dot2(ca), dot2(cb)));
}

float sdLine(float2 p, float2 a, float2 b)
{
    float2 pa = p - a;
    float2 ba = b - a;
    float h = clamp(dot(pa, ba) / dot(ba, ba), 0.0, 1.0);
    return length(pa - ba * h);
}

float sdBox(float2 p, float2 b)
{
    float2 d = abs(p) - b;
    return length(max(d, 0)) + min(max(d.x, d.y), 0.0);
}

float opSmoothUnion(float d1, float d2, float k)
{
    float h = clamp(0.5 + 0.5 * (d2 - d1) / k, 0.0, 1.0);
    return lerp(d2, d1, h) - k * h * (1.0 - h);
}

float sdCloud(float2 p, float2 a1, float2 b1, float2 a2, float2 b2, float w)
{
    float lineVal1 = sdLine(p, a1, b1);
    float lineVal2 = sdLine(p, a2, b2);
    float2 ww = float2(w * 1.5, 0.0);
    float2 left = max(a1 + ww, a2 + ww);
    float2 right = min(b1 - ww, b2 - ww);
    float2 boxCenter = (left + right) * 0.5;
    float boxH = abs(a2.y - a1.y) * 0.5;
    float boxVal = sdBox(p - boxCenter, float2(0.04, boxH)) + w;
    float u1 = opSmoothUnion(lineVal1, boxVal, 0.05);
    float u2 = opSmoothUnion(lineVal2, boxVal, 0.05);
    return min(u1, u2);
}

float4 frag(float4 position : SV_Position) : SV_Target
{
    position.y = _Resolution.y - position.y;
    float2 uv = (2.0 * position.xy - _Resolution) / _Resolution.y;
    float battery = 1.0;
    float fog = smoothstep(0.1, -0.02, abs(uv.y + 0.2));
    float3 col = float3(0.0, 0.1, 0.2);

    if (uv.y < -0.2)
    {
        uv.y = 3.0 / (abs(uv.y + 0.2) + 0.05);
        uv.x *= uv.y;
        col = lerp(col, float3(1, 0.5, 1), grid(uv, battery));
    }
    else
    {
        float fujiD = min(uv.y * 4.5 - 0.5, 1.0);
        uv.y -= battery * 1.1 - 0.51;
        float2 sunUV = uv + float2(0.75, 0.2);

        col = float3(1, 0.2, 1);
        float sunVal = sun(sunUV, battery);
        col = lerp(col, float3(1, 0.4, 0.1), sunUV.y * 2.0 + 0.2);
        col = lerp(0, col, sunVal);

        float fujiVal = sdTrapezoid(uv + float2(-0.75, 0.5),
            1.75 + pow(uv.y * uv.y, 2.1), 0.2, 0.5);
        float waveVal = uv.y + sin(uv.x * 20.0 + _Time * 2.0) * 0.05 + 0.2;
        float waveWidth = smoothstep(0.0, 0.01, waveVal);

        col = lerp(col, lerp(float3(0, 0, 0.25), float3(1, 0, 0.5), fujiD), step(fujiVal, 0.0));
        col = lerp(col, float3(1, 0.5, 1), waveWidth * step(fujiVal, 0.0));
        col = lerp(col, float3(1, 0.5, 1), 1.0 - smoothstep(0.0, 0.01, abs(fujiVal)));
        col += lerp(col, lerp(float3(1, 0.12, 0.8), float3(0, 0, 0.2),
            clamp(uv.y * 3.5 + 3.0, 0.0, 1.0)), step(0.0, fujiVal));

        float2 cloudUV = uv;
        cloudUV.x = glsl_mod(cloudUV.x + _Time * 0.1, 4.0) - 2.0;
        float ct = _Time * 0.5;
        float cloudY = -0.5;
        float c1 = sdCloud(cloudUV,
            float2(0.10 + sin(ct          + 140.500) * 0.10, cloudY),
            float2(1.05 + cos(ct * 0.9    -  36.560) * 0.10, cloudY),
            float2(0.20 + cos(ct * 0.867  + 387.165) * 0.10, 0.25 + cloudY),
            float2(0.50 + cos(ct * 0.9675 -  15.162) * 0.09, 0.25 + cloudY),
            0.075);
        cloudY = -0.6;
        float c2 = sdCloud(cloudUV,
            float2(-0.9 + cos(ct * 1.02   + 541.750) * 0.10, cloudY),
            float2(-0.5 + sin(ct * 0.9    - 316.560) * 0.10, cloudY),
            float2(-1.5 + cos(ct * 0.867  +  37.165) * 0.10, 0.25 + cloudY),
            float2(-0.6 + sin(ct * 0.9675 + 665.162) * 0.09, 0.25 + cloudY),
            0.075);
        float cloudVal = min(c1, c2);
        col = lerp(col, float3(0, 0, 0.2), 1.0 - smoothstep(0.075 - 0.0001, 0.075, cloudVal));
        col += float3(1, 1, 1) * (1.0 - smoothstep(0.0, 0.01, abs(cloudVal - 0.075)));
    }
    col += fog * fog * fog;
    col = lerp(col.r * 0.5, col, battery * 0.7);
    return float4(col, 1);
}
