namespace OpenVulScan.Tests;

public class V3005Tests
{
    [Fact]
    public Task SimpleVariableSelfAssignment()
    {
        const string source = "class C { void M() { int x = 1; x = x; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3005", "SimpleVariableSelfAssignment", source);
    }

    [Fact]
    public Task FieldAccessSelfAssignment()
    {
        const string source = "class C { int x; void M() { this.x = this.x; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3005", "FieldAccessSelfAssignment", source);
    }

    [Fact]
    public Task IndexerSelfAssignment()
    {
        const string source = "class C { void M(int[] arr, int i) { arr[i] = arr[i]; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3005", "IndexerSelfAssignment", source);
    }

    [Fact]
    public Task NestedMemberAccessSelfAssignment()
    {
        const string source = "class C { object obj; void M() { ((C)obj).obj = ((C)obj).obj; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3005", "NestedMemberAccessSelfAssignment", source);
    }

    [Fact]
    public Task MultipleSelfAssignments()
    {
        const string source = "class C { void M() { int a = 1, b = 2; a = a; b = b; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3005", "MultipleSelfAssignments", source);
    }

    [Fact]
    public Task DifferentVariables()
    {
        const string source = "class C { void M() { int x = 1, y = 2; x = y; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3005", "DifferentVariables", source);
    }

    [Fact]
    public Task CompoundAssignment()
    {
        const string source = "class C { void M() { int x = 1; x += 1; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3005", "CompoundAssignment", source);
    }

    [Fact]
    public Task DifferentFields()
    {
        const string source = "class C { int x, y; void M() { this.x = this.y; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3005", "DifferentFields", source);
    }

    [Fact]
    public Task DifferentIndexers()
    {
        const string source = "class C { void M(int[] arr, int i, int j) { arr[i] = arr[j]; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3005", "DifferentIndexers", source);
    }

    [Fact]
    public Task AssignmentWithExpression()
    {
        const string source = "class C { void M() { int x = 1; x = x + 1; } }";
        return SnapshotTestHarness.RunRuleSnapshotAsync("V3005", "AssignmentWithExpression", source);
    }
}
