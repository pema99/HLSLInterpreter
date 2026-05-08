// Name: Simple lit vert/frag
// RenderMode: VertFrag
// VertEntry: vert
// FragEntry: frag

struct VSOut
{
    float4 pos    : SV_Position;
    float3 normal : NORMAL;
};

VSOut vert(float3 pos : POSITION, float3 normal : NORMAL)
{
    VSOut o;
    o.pos = mul(_ViewProjection, float4(pos, 1.0));
    o.normal = normal;
    return o;
}

float4 frag(VSOut i) : SV_Target
{
    float3 light = normalize(float3(0.4, 0.8, 0.6));
    float ndl = saturate(dot(normalize(i.normal), light));
    float3 base = float3(0.25, 0.55, 0.95);
    return float4(base * (0.2 + 0.8 * ndl), 1);
}