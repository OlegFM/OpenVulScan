using Xunit;

namespace OpenVulScan.Tests;

public class V3168Tests
{
    [Fact]
    public Task AwaitConditionalInvocation() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3168", "await_conditional_invocation", @"
using System.Threading.Tasks;
class C
{
    Task F() => Task.CompletedTask;
    async Task M(C a)
    {
        await a?.F();
    }
}");

    [Fact]
    public Task AwaitVariableAssignedFromConditional() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3168", "await_variable_from_conditional", @"
using System.Threading.Tasks;
class C
{
    Task F() => Task.CompletedTask;
    async Task M(C a)
    {
        var t = a?.F();
        await t;
    }
}");

    [Fact]
    public Task AwaitNullLiteralAssignedTask() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3168", "await_null_literal_task", @"
using System.Threading.Tasks;
class C
{
    async Task M()
    {
        Task t = null;
        await t;
    }
}");

    [Fact]
    public Task AwaitTaskNulledOnOneBranch() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3168", "await_task_nulled_on_branch", @"
using System.Threading.Tasks;
class C
{
    async Task M(bool c)
    {
        Task t = Task.CompletedTask;
        if (c) { t = null; }
        await t;
    }
}");

    [Fact]
    public Task AwaitCheckedTaskIsSilent() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3168", "await_checked_task_silent", @"
using System.Threading.Tasks;
class C
{
    Task F() => Task.CompletedTask;
    async Task M(C a)
    {
        var t = a?.F();
        if (t != null)
        {
            await t;
        }
    }
}");

    [Fact]
    public Task AwaitFreshTaskIsSilent() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3168", "await_fresh_task_silent", @"
using System.Threading.Tasks;
class C
{
    async Task M()
    {
        await Task.CompletedTask;
    }
}");

    [Fact]
    public Task AwaitUncheckedParameterIsSilent() => SnapshotTestHarness.RunRuleSnapshotAsync(
        "V3168", "await_unchecked_parameter_silent", @"
using System.Threading.Tasks;
class C
{
    async Task M(Task t)
    {
        await t;
    }
}");
}
