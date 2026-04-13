#include "HLSLTest.hlsl"

static int a = 1337;

struct Foo
{
    int a;
    
    void Bar()
    {
        a += 1;
    }

    static int Baz()
    {
        return a;
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

interface Foo
{
    int bar();
};

struct Bar : Foo
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

int RunBar(Foo f)
{
    return f.bar();
}

[Test]
void Struct_ImplementingInterface_CanBePassedAsInterfaceParameter()
{
    Bar b;
    b.a = 1;
    ASSERT(RunBar(b) == 1);

    Baz c;
    c.a = 1;
    c.b = 3;
    ASSERT(RunBar(c) == 9);
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