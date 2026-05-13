namespace NullSpike;

internal enum NullState
{
    Unknown,
    NotNull,
    DefinitelyNull,
    MaybeNull,
}

internal static class NullStateLattice
{
    public static NullState Join(NullState a, NullState b)
    {
        if (a == b) return a;
        if (a == NullState.Unknown) return b;
        if (b == NullState.Unknown) return a;
        return NullState.MaybeNull;
    }

    public static string ToLabel(this NullState state) => state switch
    {
        NullState.Unknown => "?",
        NullState.NotNull => "!",
        NullState.DefinitelyNull => "⊥",
        NullState.MaybeNull => "⊤",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };
}
