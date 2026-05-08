// Name: Breathing vertices
// RenderMode: VertFrag
// VertEntry: vert
// FragEntry: frag

struct VSOut
{
    float4 pos    : SV_Position;
    float3 normal : NORMAL;
    float3 color  : COLOR;
};

float3x3 rotY(float a)
{
    float c = cos(a), s = sin(a);
    return float3x3(
        c, 0, s,
        0, 1, 0,
       -s, 0, c);
}

VSOut vert(float3 pos : POSITION, float3 normal : NORMAL)
{
    float pulse = 0.18 * sin(_Time * 2.5 + pos.y * 3.0);
    float3 newPos = pos + normal * pulse;

    VSOut o;
    o.pos = mul(_Projection, mul(_View, float4(newPos, 1.0)));
    o.normal = normal;
    o.color = 0.5 + 0.5 * sin(newPos * 2.5 + float3(0, 2, 4) + _Time);
    return o;
}

float4 frag(VSOut i) : SV_Target
{
    float3 light = normalize(float3(0.4, 0.8, 0.6));
    float ndl = saturate(dot(normalize(i.normal), light));
    return float4(i.color * (0.25 + 0.75 * ndl), 1);
}
