namespace OpenVulScan.Tests;

public class V3110Tests
{
    [Fact]
    public Task ClassWithEqualsNoGetHashCode()
    {
        const string source = """
            class C
            {
                public override bool Equals(object obj) => false;
            }
            """;
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3110", "ClassWithEqualsNoGetHashCode", source);
    }

    [Fact]
    public Task ClassWithGetHashCodeNoEquals()
    {
        const string source = """
            class C
            {
                public override int GetHashCode() => 0;
            }
            """;
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3110", "ClassWithGetHashCodeNoEquals", source);
    }

    [Fact]
    public Task StructWithEqualsNoGetHashCode()
    {
        const string source = """
            struct S
            {
                public override bool Equals(object obj) => false;
            }
            """;
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3110", "StructWithEqualsNoGetHashCode", source);
    }

    [Fact]
    public Task StructWithGetHashCodeNoEquals()
    {
        const string source = """
            struct S
            {
                public override int GetHashCode() => 0;
            }
            """;
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3110", "StructWithGetHashCodeNoEquals", source);
    }

    [Fact]
    public Task ClassWithEqualsOverloadedNotObject()
    {
        const string source = """
            class C
            {
                public bool Equals(C other) => false;
            }
            """;
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3110", "ClassWithEqualsOverloadedNotObject", source);
    }

    [Fact]
    public Task ClassWithBothEqualsAndGetHashCode()
    {
        const string source = """
            class C
            {
                public override bool Equals(object obj) => false;
                public override int GetHashCode() => 0;
            }
            """;
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3110", "ClassWithBothEqualsAndGetHashCode", source);
    }

    [Fact]
    public Task ClassWithNeitherEqualsNorGetHashCode()
    {
        const string source = """
            class C
            {
            }
            """;
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3110", "ClassWithNeitherEqualsNorGetHashCode", source);
    }

    [Fact]
    public Task InterfaceWithNeither()
    {
        const string source = """
            interface I
            {
            }
            """;
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3110", "InterfaceWithNeither", source);
    }

    [Fact]
    public Task EnumShouldBeIgnored()
    {
        const string source = """
            enum E
            {
                A, B
            }
            """;
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3110", "EnumShouldBeIgnored", source);
    }

    [Fact]
    public Task ClassWithOnlyGetHashCodeOverload()
    {
        const string source = """
            class C
            {
                public int GetHashCode(string s) => 0;
            }
            """;
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3110", "ClassWithOnlyGetHashCodeOverload", source);
    }
}
