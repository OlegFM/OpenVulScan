// Synthetic corpus: realistic null-safe patterns. The NRE quartet
// (V3080, V3105, V3153, V3168) must produce ZERO diagnostics here.
using System;
using System.Threading.Tasks;

namespace Synthetic;

public class GuardClauses
{
    public int IfGuard(string s)
    {
        if (s != null)
        {
            return s.Length;
        }
        return 0;
    }

    public int IsNotNullGuard(string s)
    {
        if (s is not null)
        {
            return s.Length;
        }
        return 0;
    }

    public int EarlyReturn(string s)
    {
        if (s == null) { return 0; }
        return s.Length;
    }

    public int IsNullEarlyReturn(string s)
    {
        if (s is null) { return 0; }
        return s.Length;
    }

    public int AndChainGuard(string a, string b)
    {
        if (a != null && b != null)
        {
            return a.Length + b.Length;
        }
        return 0;
    }

    public int OrNegativeGuard(string s)
    {
        if (s == null || s.Length == 0)
        {
            return 0;
        }
        return s.Length;
    }
}

public class Reassignment
{
    public int AssignedBeforeUse()
    {
        string s = null;
        s = "value";
        return s.Length;
    }

    public int AssignedOnAllBranches(bool c)
    {
        string s = null;
        if (c) { s = "a"; } else { s = "b"; }
        return s.Length;
    }

    public int LoopReassignment(int n)
    {
        string s = "seed";
        for (int i = 0; i < n; i++)
        {
            var len = s.Length;
            s = len.ToString();
        }
        return s.Length;
    }
}

public class Coalescing
{
    public int CoalesceLiteral(string s)
    {
        var t = s ?? "fallback";
        return t.Length;
    }

    public int CoalesceOnConditional(GuardClauses g)
    {
        var t = g?.ToString() ?? "fallback";
        return t.Length;
    }

    public int InlineCoalesceDeref(string s)
    {
        return (s ?? "fallback").Length;
    }
}

public class ConditionalChains
{
    public string Inner;

    public int? SafeChain(ConditionalChains c)
    {
        return c?.Inner?.Length;
    }

    public int CheckedConditionalResult(ConditionalChains c)
    {
        var inner = c?.Inner;
        if (inner != null)
        {
            return inner.Length;
        }
        return 0;
    }
}

public class AsyncPatterns
{
    public Task Work() => Task.CompletedTask;

    public async Task AwaitFresh()
    {
        await Task.CompletedTask;
    }

    public async Task AwaitChecked(AsyncPatterns p)
    {
        var t = p?.Work();
        if (t != null)
        {
            await t;
        }
    }

    public async Task AwaitParameter(Task t)
    {
        await t;
    }
}

public class FieldPatterns
{
    private string _name;

    public int GuardedField()
    {
        if (_name != null)
        {
            return _name.Length;
        }
        return 0;
    }

    public int AssignedField()
    {
        _name = "value";
        return _name.Length;
    }
}
