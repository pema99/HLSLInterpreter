#include "HLSLTest.hlsl"

static int g_a = 1337;

struct Foo
{
    int a;
    
    void Bar()
    {
        a += 1;
    }

    static int Baz()
    {
        return g_a;
    }

    int Boo(int b)
    {
        return a + b;
    }
};

[Test]
void CallInstanceMethod_OnStruct_ModifiesLocalState()
{
    Foo f;
    f.a = 12;

    f.Bar();
    ASSERT(f.a == 13);

    f.Bar();
    ASSERT(f.a == 14);
}

[Test]
void CallInstanceMethodWithParameter_OnStruct_Succeeds()
{
    Foo f;
    f.a = 12;

    ASSERT(f.Boo(5) == 17);
}


[Test]
void CallStaticMethod_OnStruct_RefersToGlobalState()
{
    ASSERT(Foo::Baz() == 1337);
}

[Test]
void StructDefaultInitializer_ReadField_IsZero()
{
    Foo f = (Foo)0;
    ASSERT(f.a == 0);
}

struct Single
{
    int baz;
};

struct Dual
{
    int foo;
    Single baz;
};

[Test]
void StructScalarInitializer_ReadField_MatchesValue()
{
    Dual f = (Dual)13;
    ASSERT(f.foo == 13);
    ASSERT(f.baz.baz == 13);
}

[Test]
void StructArrayInitializer_ReadField_MatchesValues()
{
    Dual f = {2,1};
    ASSERT(f.foo == 2);
    ASSERT(f.baz.baz == 1);
}

struct weird
{
    int a;
    int b;
    
    float getA() { return this.a; }

    void reset()
    { 
        this = (weird)0; 
    }
};

[Test]
void ThisKeyword_InsideMethod_AccessesInstance()
{
    weird foo;
    foo.a = 1;
    foo.b = 2;
    ASSERT(foo.getA() == 1);
}

[Test]
void ThisKeyword_Assignment_ResetsMembers()
{
    weird foo;
    foo.a = 1;
    foo.b = 2;
    foo.reset();
    ASSERT(foo.a == 0);
    ASSERT(foo.b == 0);
}

[Test]
void ThisKeyword_InsideMethod_GetsVaryingValue()
{
    weird foo;
    foo.a = WaveGetLaneIndex();
    foo.b = 2;
    ASSERT(!WaveActiveAllEqual(foo.getA()));
}


[Test]
void ThisKeyword_AssignmentInVaryingControlFlow_ResetsForSomeThreads()
{
    weird foo;
    foo.a = 1;
    foo.b = 2;
    if (WaveGetLaneIndex() == 0)
        foo.reset();
    ASSERT(WaveReadLaneAt(foo.a, 0) == 0);
    ASSERT(WaveReadLaneAt(foo.b, 0) == 0);
    ASSERT(WaveReadLaneAt(foo.a, 1) == 1);
    ASSERT(WaveReadLaneAt(foo.b, 1) == 2);
}

interface IFoo
{
    int bar();
};

struct Bar : IFoo
{
    int a;
    int bar() { return a; }
    int bom() { return 5; }
};

struct Baz : Bar
{
    int b;
    int bar() { return a + bom() + b; }
};

[Test]
void CanConstruct_Struct_ImplementingInterface()
{
    Bar b;
    b.a = 1;
    ASSERT(b.bar() == 1);
}

[Test]
void Struct_InheritingFromOtherStruct_InheritsMembers()
{
    Baz b;
    b.a = 1;
    b.b = 3;
    ASSERT(b.bar() == 9);
    ASSERT(b.bom() == 5);
}

[Test]
void Struct_ImplementingInterface_DispatchesToCorrectOverride()
{
    Bar b;
    b.a = 1;
    ASSERT(b.bar() == 1);

    Baz c;
    c.a = 1;
    c.b = 3;
    ASSERT(c.bar() == 9);
}

// --- Inherited static methods ---

struct Vehicle
{
    int speed;

    static int GetCategory() { return 7; }

    int GetSpeed() { return speed; }

    // Calls inherited static GetCategory() and own GetSpeed() from within a method
    int Describe() { return GetCategory() * 100 + GetSpeed(); }
};

struct Car : Vehicle
{
    int doors;
};

struct Truck : Car
{
    int payload;
};

[Test]
void InheritedStaticMethod_CalledDirectlyOnBase()
{
    ASSERT(Vehicle::GetCategory() == 7);
}

[Test]
void InheritedInstanceMethod_CalledOnDerivedStruct()
{
    Car c;
    c.speed = 5;
    c.doors = 4;
    ASSERT(c.GetSpeed() == 5);
}

[Test]
void InheritedMethod_CallsImplicitStaticFromBody()
{
    // Describe() is inherited from Vehicle; it calls GetCategory() (static) + GetSpeed() (instance)
    Car c;
    c.speed = 3;
    ASSERT(c.Describe() == 703); // 7*100 + 3
}

[Test]
void DeepInheritance_InheritsFieldsAndMethods()
{
    Truck t;
    t.speed = 9;
    t.doors = 2;
    t.payload = 1000;
    ASSERT(t.GetSpeed() == 9);
    ASSERT(t.Describe() == 709); // 7*100 + 9
}

// --- Namespace + struct inheritance ---

namespace Shapes
{
    struct Shape
    {
        int color;
        static int GetDimensions() { return 2; }
        int GetColor() { return color; }
    };

    struct Circle : Shape
    {
        int radius;
        int Area() { return radius * radius; } // simplified: r*r instead of pi*r*r
    };

    struct LabeledCircle : Circle
    {
        int label;
    };
}

[Test]
void Namespace_InheritedFields_AreAccessible()
{
    Shapes::Circle c;
    c.color = 5;
    c.radius = 3;
    ASSERT(c.color == 5);
    ASSERT(c.radius == 3);
}

[Test]
void Namespace_InheritedInstanceMethod_Works()
{
    Shapes::Circle c;
    c.color = 5;
    c.radius = 3;
    ASSERT(c.GetColor() == 5);
    ASSERT(c.Area() == 9);
}

[Test]
void Namespace_InheritedStaticMethod_CalledOnBase()
{
    ASSERT(Shapes::Shape::GetDimensions() == 2);
}

[Test]
void Namespace_DeepInheritance_InheritsAllFields()
{
    Shapes::LabeledCircle lc;
    lc.color = 1;
    lc.radius = 4;
    lc.label = 99;
    ASSERT(lc.GetColor() == 1);
    ASSERT(lc.Area() == 16);
    ASSERT(lc.label == 99);
}

// --- Inline struct definitions ---
[Test]
void StructType_DefinedInline_CanAccessField()
{
    struct Foo { int a; };
    Foo f;
    f.a = 1;
    ASSERT(f.a == 1);

    struct Bar { int b; } h;
    h.b = 2;
    ASSERT(h.b == 2);
}

[Test]
void StructType_DefinedInline_CanCallInstanceMethods()
{
    struct Foo { int a; int boo() { return a; } };
    Foo f;
    f.a = 1;
    ASSERT(f.boo() == 1);

    struct Bar { int a; int boo() { return a*2; } } h;
    h.a = 2;
    ASSERT(h.boo() == 4);
}

[Test]
void StructType_DefinedInline_CanCallStaticMethods()
{
    struct Foo { static int boo() { return 1; } };
    Foo f;
    ASSERT(Foo::boo() == 1);

    struct Bar { static int boo() { return 2; } } h;
    ASSERT(Bar::boo() == 2);
}

[Test]
void StructType_DefinedInline_RespectsScoping()
{
    {
        struct Foo { int boo() { return 1; } };
        Foo a;
        ASSERT(a.boo() == 1);
    }
    {
        struct Foo { int boo() { return 2; } };
        Foo a;
        ASSERT(a.boo() == 2);
    }
}

void InnerStruct()
{
    struct Foo { int boo() { return 2; } };
    Foo a;
    ASSERT(a.boo() == 2);
}

[Test]
void StructType_DefinedInlineInFunction_DoesNotLinger()
{
    struct Foo { int boo() { return 1; } };
    Foo a;
    ASSERT(a.boo() == 1);

    InnerStruct();
    ASSERT(a.boo() == 1);

    Foo b;
    ASSERT(b.boo() == 1);
}

// --- Inline struct initializers ---

[Test]
void StructType_DefinedInline_WithScalarInitializer()
{
    struct Foo { int a; int b; } f = (Foo)7;
    ASSERT(f.a == 7);
    ASSERT(f.b == 7);
}

[Test]
void StructType_DefinedInline_WithArrayInitializer()
{
    struct Foo { int a; int b; } f = {3, 5};
    ASSERT(f.a == 3);
    ASSERT(f.b == 5);
}

// --- Multiple declarators ---

[Test]
void StructType_DefinedInline_MultipleDeclarators_AreIndependent()
{
    struct Foo { int a; } x, y;
    x.a = 1;
    y.a = 2;
    ASSERT(x.a == 1);
    ASSERT(y.a == 2);
}

// --- Static method lingering ---

void InnerStaticStruct()
{
    struct Qux { static int boo() { return 2; } };
}

[Test]
void StructType_InlineStaticMethod_DoesNotLingerAfterFunctionReturn()
{
    struct Qux { static int boo() { return 1; } };
    ASSERT(Qux::boo() == 1);

    InnerStaticStruct();
    ASSERT(Qux::boo() == 1);
}

void InnerRegistersFirst()
{
    struct Ghost { static int val() { return 2; } };
}

[Test]
void StructType_InlineStaticMethod_LateRegistration_NotPoisoned()
{
    InnerRegistersFirst();
    struct Ghost { static int val() { return 1; } };
    ASSERT(Ghost::val() == 1);
}

// Tests inspired by DXC tools/clang/test/HLSLFileCheck/hlsl/types/struct/
// and tools/clang/test/HLSLFileCheck/hlsl/classes/

// ============================================================================
// CLASS KEYWORD
// Inspired by DXC classes/class.hlsl: the 'class' keyword works the same as
// 'struct' in HLSL.
// ============================================================================

class MyPoint
{
    float x;
    float y;

    float Length()
    {
        return sqrt(x * x + y * y);
    }

    static MyPoint Zero()
    {
        MyPoint p;
        p.x = 0.0;
        p.y = 0.0;
        return p;
    }
};

[Test]
void Struct_ClassKeyword_FieldsReadAndWrite()
{
    MyPoint p;
    p.x = 3.0;
    p.y = 4.0;
    ASSERT(p.x == 3.0 && p.y == 4.0);
}

[Test]
void Struct_ClassKeyword_InstanceMethod_ReturnsCorrectValue()
{
    MyPoint p;
    p.x = 3.0;
    p.y = 4.0;
    ASSERT(abs(p.Length() - 5.0) < 0.001);
}

[Test]
void Struct_ClassKeyword_StaticMethod_ReturnsZeroPoint()
{
    MyPoint p = MyPoint::Zero();
    ASSERT(p.x == 0.0 && p.y == 0.0);
}

// ============================================================================
// SELF-ASSIGNMENT
// Inspired by DXC types/struct/self_copy.hlsl: assigning a struct to itself.
// ============================================================================

struct Node
{
    float value;
    int index;
};

[Test]
void Struct_SelfAssignment_NoCorruption()
{
    Node n;
    n.value = 3.14;
    n.index = 7;
    n = n;
    ASSERT(abs(n.value - 3.14) < 0.001);
    ASSERT(n.index == 7);
}

// ============================================================================
// NESTED STRUCT ACCESS
// ============================================================================

struct Inner
{
    int x;
};

struct Middle
{
    Inner inner;
    float y;
};

struct Outer
{
    Middle mid;
    bool flag;
};

[Test]
void Struct_NestedAccess_DeepChain_ReadAndWrite()
{
    Outer o;
    o.mid.inner.x = 42;
    o.mid.y = 1.5;
    o.flag = true;

    ASSERT(o.mid.inner.x == 42);
    ASSERT(abs(o.mid.y - 1.5) < 0.001);
    ASSERT(o.flag == true);
}

[Test]
void Struct_NestedAccess_ModifyNestedField_OtherFieldsUnchanged()
{
    Outer o;
    o.mid.inner.x = 10;
    o.mid.y = 2.0;
    o.flag = false;

    o.mid.inner.x = 99;

    ASSERT(o.mid.inner.x == 99);
    ASSERT(abs(o.mid.y - 2.0) < 0.001); // unchanged
    ASSERT(o.flag == false);             // unchanged
}

// ============================================================================
// STRUCT WITH ARRAY MEMBER
// ============================================================================

struct WithArray
{
    int data[4];
    int count;
};

[Test]
void Struct_ArrayMember_InitAndAccess()
{
    WithArray wa;
    wa.count = 4;
    wa.data[0] = 10;
    wa.data[1] = 20;
    wa.data[2] = 30;
    wa.data[3] = 40;

    ASSERT(wa.count == 4);
    ASSERT(wa.data[0] == 10);
    ASSERT(wa.data[3] == 40);
}

[Test]
void Struct_ArrayMember_SumOfElements()
{
    WithArray wa;
    wa.count = 4;
    wa.data[0] = 1;
    wa.data[1] = 2;
    wa.data[2] = 3;
    wa.data[3] = 4;

    int sum = 0;
    for (int i = 0; i < wa.count; i++)
        sum += wa.data[i];

    ASSERT(sum == 10);
}

[Test]
void Struct_ArrayMember_WriteInsideLoop()
{
    WithArray wa;
    wa.count = 4;
    for (int i = 0; i < 4; i++)
        wa.data[i] = i * i;

    ASSERT(wa.data[0] == 0);
    ASSERT(wa.data[1] == 1);
    ASSERT(wa.data[2] == 4);
    ASSERT(wa.data[3] == 9);
}

// ============================================================================
// ARRAY OF STRUCTS
// Inspired by DXC types/struct/structArray.hlsl and types/array/arrayOfStruct.hlsl
// ============================================================================

struct Vec2
{
    float x;
    float y;
};

[Test]
void Struct_ArrayOfStructs_InitAndAccess()
{
    Vec2 points[3];
    points[0].x = 1.0; points[0].y = 0.0;
    points[1].x = 0.0; points[1].y = 1.0;
    points[2].x = 1.0; points[2].y = 1.0;

    ASSERT(points[0].x == 1.0 && points[0].y == 0.0);
    ASSERT(points[1].x == 0.0 && points[1].y == 1.0);
    ASSERT(points[2].x == 1.0 && points[2].y == 1.0);
}

[Test]
void Struct_ArrayOfStructs_IterateAndAccumulate()
{
    Vec2 points[4];
    points[0].x = 1.0; points[0].y = 2.0;
    points[1].x = 3.0; points[1].y = 4.0;
    points[2].x = 5.0; points[2].y = 6.0;
    points[3].x = 7.0; points[3].y = 8.0;

    float sumX = 0.0;
    float sumY = 0.0;
    for (int i = 0; i < 4; i++)
    {
        sumX += points[i].x;
        sumY += points[i].y;
    }

    ASSERT(sumX == 16.0); // 1+3+5+7
    ASSERT(sumY == 20.0); // 2+4+6+8
}

[Test]
void Struct_ArrayOfStructs_ModifyInsideLoop()
{
    Vec2 points[3];
    for (int i = 0; i < 3; i++)
    {
        points[i].x = (float)i;
        points[i].y = (float)(i * 2);
    }

    ASSERT(points[0].x == 0.0 && points[0].y == 0.0);
    ASSERT(points[1].x == 1.0 && points[1].y == 2.0);
    ASSERT(points[2].x == 2.0 && points[2].y == 4.0);
}

// ============================================================================
// STRUCT RETURNED FROM FUNCTION
// Inspired by DXC types/struct/structArray.hlsl
// ============================================================================

Vec2 AddVec2(Vec2 a, Vec2 b)
{
    Vec2 result;
    result.x = a.x + b.x;
    result.y = a.y + b.y;
    return result;
}

[Test]
void Struct_ReturnedFromFunction_HasCorrectValues()
{
    Vec2 a; a.x = 1.0; a.y = 2.0;
    Vec2 b; b.x = 3.0; b.y = 4.0;
    Vec2 c = AddVec2(a, b);
    ASSERT(c.x == 4.0 && c.y == 6.0);
}

[Test]
void Struct_ChainedReturnFromFunction_HasCorrectValues()
{
    Vec2 a; a.x = 1.0; a.y = 1.0;
    Vec2 b; b.x = 2.0; b.y = 2.0;
    Vec2 c; c.x = 3.0; c.y = 3.0;
    Vec2 result = AddVec2(AddVec2(a, b), c);
    ASSERT(result.x == 6.0 && result.y == 6.0);
}

// ============================================================================
// CLASS WITH METHOD OVERLOADING
// Inspired by DXC classes/class_method_overload.hlsl
// ============================================================================

class Calculator
{
    int Add(int a, int b)    { return a + b; }
    float Add(float a, float b) { return a + b; }
    int Multiply(int a, int b) { return a * b; }
};

[Test]
void Struct_ClassMethodOverload_IntAdd_UsesIntOverload()
{
    Calculator c;
    ASSERT(c.Add(3, 4) == 7);
}

[Test]
void Struct_ClassMethodOverload_FloatAdd_UsesFloatOverload()
{
    Calculator c;
    ASSERT(abs(c.Add(1.5, 2.5) - 4.0) < 0.001);
}

[Test]
void Struct_ClassMethodOverload_Multiply_Correct()
{
    Calculator c;
    ASSERT(c.Multiply(6, 7) == 42);
}

// ============================================================================
// STRUCT COPY AND INDEPENDENT MODIFICATION
// (Self-copy test from self_copy.hlsl - both nested structs)
// ============================================================================

struct NestedSelf
{
    float s;
    Inner n;
};

NestedSelf g_nestedSelf;

[Test]
void Struct_CopyFromGlobal_AndModifyLocal()
{
    g_nestedSelf.s = 1.0;
    g_nestedSelf.n.x = 10;

    NestedSelf local = g_nestedSelf;
    local.s = local.n.x; // local.n.x is 10

    ASSERT(abs(local.s - 10.0) < 0.001);
    ASSERT(local.n.x == 10);
}

// ============================================================================
// EMPTY STRUCT
// Inspired by DXC types/struct/empty.hlsl
// ============================================================================

struct Empty {};

[Test]
void Struct_EmptyStruct_CanBeCreated()
{
    Empty e = (Empty)0;
    ASSERT(true); // Just verify no crash
}

// ============================================================================
// STRUCT WITH DEFAULT ZERO INITIALIZATION
// ============================================================================

struct FullInit
{
    int a;
    float b;
    int3 c;
};

[Test]
void Struct_ZeroInitialization_AllFieldsZero()
{
    FullInit fi = (FullInit)0;
    ASSERT(fi.a == 0);
    ASSERT(fi.b == 0.0);
    ASSERT(fi.c.x == 0 && fi.c.y == 0 && fi.c.z == 0);
}
