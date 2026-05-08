// Name: Simple lit vert/frag
// RenderMode: VertFrag
// VertEntry: vert
// FragEntry: frag

struct VSOut
{
    float4 pos      : SV_Position;
    float3 worldPos : TEXCOORD0;
    float3 normal   : NORMAL;
};

VSOut vert(float3 pos : POSITION, float3 normal : NORMAL)
{
    VSOut o;
    o.pos = mul(_Projection, mul(_View, float4(pos, 1.0)));
    o.worldPos = pos;
    o.normal = normal;
    return o;
}

float4 frag(VSOut i) : SV_Target
{
    float3 camPos = -mul(_View._m03_m13_m23, (float3x3)_View);

    float3 N = normalize(i.normal);
    float3 L = normalize(float3(0.4, 0.8, 0.6));
    float3 V = normalize(camPos - i.worldPos);
    float3 R = reflect(-L, N);

    float3 ambient  = float3(0.05, 0.11, 0.19);
    float3 diffCol  = float3(0.25, 0.55, 0.95);
    float3 specCol  = float3(1, 1, 1);

    float diff = saturate(dot(N, L));
    float spec = pow(saturate(dot(R, V)), 32.0);

    float3 color = ambient + diffCol * diff + specCol * spec * 0.6;
    return float4(color, 1);
}
