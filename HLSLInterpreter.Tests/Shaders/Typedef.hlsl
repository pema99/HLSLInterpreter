
// Basic scalar typedef
typedef float MyFloat;
typedef int MyInt;
typedef uint MyUint;

[Test]
void Typedef_ScalarVariable()
{
    MyFloat f = 3.14;
    ASSERT(f > 3.0 && f < 3.2);
}

[Test]
void Typedef_ScalarArithmetic()
{
    MyInt a = 10;
    MyInt b = 4;
    ASSERT(a + b == 14);
    ASSERT(a - b == 6);
    ASSERT(a * b == 40);
}

[Test]
void Typedef_ExplicitCast()
{
    float f = 7.9;
    MyInt i = (MyInt)f;
    ASSERT(i == 7);
}

[Test]
void Typedef_ImplicitConversion()
{
    MyFloat f = 5;   // int -> float
    MyInt i = f;     // float -> int (truncates)
    ASSERT(i == 5);
}

// Vector typedef
typedef float2 Vec2;
typedef float4 Color;
typedef int3 IVec3;

[Test]
void Typedef_VectorVariable()
{
    Vec2 v = Vec2(1.0, 2.0);
    ASSERT(v.x == 1.0 && v.y == 2.0);
}

[Test]
void Typedef_VectorArithmetic()
{
    Vec2 a = Vec2(1.0, 2.0);
    Vec2 b = Vec2(3.0, 4.0);
    Vec2 c = a + b;
    ASSERT(c.x == 4.0 && c.y == 6.0);
}

[Test]
void Typedef_VectorCast()
{
    Color c = Color(0.1, 0.2, 0.3, 1.0);
    float4 raw = (float4)c;
    ASSERT(abs(raw.x - 0.1) < 0.001 && abs(raw.w - 1.0) < 0.001);
}

[Test]
void Typedef_VectorCastBetweenAliases()
{
    Color c = Color(1.5, 2.5, 3.5, 4.5);
    IVec3 iv = (IVec3)c;   // truncates to int3 of first 3 components
    ASSERT(iv.x == 1 && iv.y == 2 && iv.z == 3);
}

// Matrix typedef
typedef float2x2 Mat2;
typedef float3x3 Mat3;

[Test]
void Typedef_MatrixVariable()
{
    Mat2 m = Mat2(1.0, 2.0, 3.0, 4.0);
    ASSERT(m[0][0] == 1.0 && m[0][1] == 2.0);
    ASSERT(m[1][0] == 3.0 && m[1][1] == 4.0);
}

[Test]
void Typedef_MatrixCast()
{
    Mat2 m = Mat2(1.0, 2.0, 3.0, 4.0);
    float2x2 raw = (float2x2)m;
    ASSERT(raw[0][0] == 1.0 && raw[1][1] == 4.0);
}

// Chained (alias of alias)
typedef MyFloat MyFloat2;
typedef Vec2 MyVec2;

[Test]
void Typedef_ChainedScalar()
{
    MyFloat2 f = 42.0;
    ASSERT(f == 42.0);
}

[Test]
void Typedef_ChainedVector()
{
    MyVec2 v = MyVec2(7.0, 8.0);
    ASSERT(v.x == 7.0 && v.y == 8.0);
}

// Typedef used as function parameter type
typedef float3 Normal;

float dot_normals(Normal a, Normal b)
{
    return dot(a, b);
}

[Test]
void Typedef_FunctionParam()
{
    Normal n1 = Normal(1.0, 0.0, 0.0);
    Normal n2 = Normal(0.0, 1.0, 0.0);
    float d = dot_normals(n1, n2);
    ASSERT(d == 0.0);
}

[Test]
void Typedef_FunctionParamWithBuiltinArg()
{
    float3 a = float3(1.0, 0.0, 0.0);
    float3 b = float3(1.0, 0.0, 0.0);
    float d = dot_normals(a, b);
    ASSERT(d == 1.0);
}

// Typedef used in array cast
typedef float MyScalar;

[Test]
void Typedef_InArrayCast()
{
    float4 v = float4(1.0, 2.0, 3.0, 4.0);
    MyScalar arr[4] = (MyScalar[4])v;
    ASSERT(arr[0] == 1.0 && arr[3] == 4.0);
}

// Multiple names in one typedef
typedef float AliasA, AliasB;

[Test]
void Typedef_MultipleNames()
{
    AliasA a = 1.0;
    AliasB b = 2.0;
    ASSERT(a + b == 3.0);
}

// ---- Typedef with struct types ----
struct Pixel { float r; float g; float b; float a; };
typedef Pixel RGBA;

[Test]
void Typedef_StructAlias_Variable()
{
    RGBA c;
    c.r = 1.0;
    c.g = 0.5;
    c.b = 0.25;
    c.a = 1.0;
    ASSERT(c.r == 1.0 && c.g == 0.5 && c.b == 0.25 && c.a == 1.0);
}

[Test]
void Typedef_StructAlias_Assignment()
{
    // Assign a Pixel to an RGBA variable and vice versa — they are the same type.
    Pixel p;
    p.r = 0.2; p.g = 0.4; p.b = 0.6; p.a = 0.8;
    RGBA c = p;
    ASSERT(c.r == 0.2 && c.g == 0.4);
}

[Test]
void Typedef_StructAlias_FunctionParam()
{
    RGBA c;
    c.r = 0.0; c.g = 1.0; c.b = 0.0; c.a = 1.0;
    // Pass RGBA where a Pixel is expected — same underlying type.
    Pixel p = c;
    ASSERT(p.g == 1.0);
}

// Chained struct typedef
typedef RGBA Color32;

[Test]
void Typedef_ChainedStructAlias()
{
    Color32 col;
    col.r = 1.0; col.g = 0.0; col.b = 0.0; col.a = 1.0;
    ASSERT(col.r == 1.0 && col.b == 0.0);
}

// ---- Typedef used as function return type ----
typedef float2 TexCoord;

TexCoord ScaleUV(TexCoord uv, float scale)
{
    return uv * scale;
}

[Test]
void Typedef_ReturnType()
{
    TexCoord uv = TexCoord(0.5, 0.25);
    TexCoord scaled = ScaleUV(uv, 2.0);
    ASSERT(abs(scaled.x - 1.0) < 0.001 && abs(scaled.y - 0.5) < 0.001);
}

// ---- Typedef inside a namespace ----
namespace Units
{
    typedef float Meters;
    typedef float Seconds;
    typedef float2 Velocity; // meters per second in x,y
}

[Test]
void Typedef_Namespace_ScalarTypes()
{
    Units::Meters dist = 100.0;
    Units::Seconds time = 9.58;
    float speed = dist / time;
    ASSERT(speed > 10.0 && speed < 11.0);
}

[Test]
void Typedef_Namespace_VectorType()
{
    Units::Velocity v = Units::Velocity(3.0, 4.0);
    float spd = sqrt(v.x * v.x + v.y * v.y);
    ASSERT(abs(spd - 5.0) < 0.001);
}

// Typedef to a numeric constructor called through a namespace-qualified alias
namespace Aliases
{
    typedef float4 Vec4;
}

[Test]
void Typedef_Namespace_ConstructorCall()
{
    Aliases::Vec4 v = Aliases::Vec4(1.0, 2.0, 3.0, 4.0);
    ASSERT(v.x == 1.0 && v.w == 4.0);
}

// ---- Typedef used in array declaration and as array index ----
typedef int MyIndex;

[Test]
void Typedef_ArrayDeclaration()
{
    MyIndex elems[4];
    elems[0] = 10;
    elems[1] = 20;
    elems[2] = 30;
    elems[3] = 40;
    ASSERT(elems[0] + elems[3] == 50);
}

[Test]
void Typedef_ArrayIndex()
{
    int elems[4];
    elems[0] = 10;
    elems[1] = 20;
    elems[2] = 30;
    elems[3] = 40;
    MyIndex i = 1;
    MyIndex j = 3;
    ASSERT(elems[i] == 20 && elems[j] == 40);
}

// ---- Typedef used as loop variable ----
[Test]
void Typedef_LoopVariable()
{
    MyInt sum = 0;
    for (MyInt i = 0; i < 5; i++)
        sum += i;
    ASSERT(sum == 10);
}
