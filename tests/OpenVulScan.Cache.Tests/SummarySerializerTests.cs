using System.Collections.Immutable;
using System.IO;
using Xunit;

namespace OpenVulScan.Tests;

public class SummarySerializerTests
{
    private static readonly ImmutableArray<MethodSummary> s_templates =
    [
        // 1. Pure getter that never returns null.
        new MethodSummary(
            "M:App.Config.GetName",
            NullState.NotNull,
            OutParameters: [],
            Throws: [],
            IsPure: true,
            TaintPassThrough: []),

        // 2. Factory that may return null.
        new MethodSummary(
            "M:App.Repo.FindUser(System.Int32)",
            NullState.MaybeNull,
            OutParameters: [],
            Throws: [],
            IsPure: false,
            TaintPassThrough: []),

        // 3. TryGet pattern: bool return, out-parameter may be null.
        new MethodSummary(
            "M:App.Cache.TryGet(System.String,App.Entry@)",
            NullState.NotNull,
            OutParameters: [new ParameterNullability(1, NullState.MaybeNull)],
            Throws: [],
            IsPure: false,
            TaintPassThrough: []),

        // 4. Validator that throws.
        new MethodSummary(
            "M:App.Validator.Check(System.String)",
            NullState.Unknown,
            OutParameters: [],
            Throws: ["System.ArgumentNullException", "System.FormatException"],
            IsPure: false,
            TaintPassThrough: []),

        // 5. Identity-shaped method: argument 0 flows to the return value.
        new MethodSummary(
            "M:App.Text.Normalize(System.String)",
            NullState.NotNull,
            OutParameters: [],
            Throws: [],
            IsPure: true,
            TaintPassThrough: [0]),
    ];

    [Fact]
    public void Roundtrip_FiveTemplateSummaries_PreservesEverything()
    {
        var bytes = SummarySerializer.Serialize(s_templates);
        var restored = SummarySerializer.Deserialize(bytes);

        Assert.Equal(s_templates.Length, restored.Length);
        for (int i = 0; i < s_templates.Length; i++)
        {
            var expected = s_templates[i];
            var actual = restored[i];
            Assert.Equal(expected.MethodId, actual.MethodId);
            Assert.Equal(expected.ReturnNullability, actual.ReturnNullability);

            // ImmutableArray<T> implements IEquatable via reference identity of the backing
            // array, so compare as sequences to get structural equality.
            Assert.Equal<ParameterNullability>(expected.OutParameters, actual.OutParameters);
            Assert.Equal<string>(expected.Throws, actual.Throws);
            Assert.Equal(expected.IsPure, actual.IsPure);
            Assert.Equal<int>(expected.TaintPassThrough, actual.TaintPassThrough);
        }
    }

    [Fact]
    public void Roundtrip_EmptyCollection_YieldsEmpty()
    {
        var bytes = SummarySerializer.Serialize([]);
        var restored = SummarySerializer.Deserialize(bytes);

        Assert.Empty(restored);
    }

    [Fact]
    public void Deserialize_UnknownFormatVersion_Throws()
    {
        var bytes = SummarySerializer.SerializeWithVersion(s_templates, formatVersion: 999);

        Assert.Throws<InvalidDataException>(() => SummarySerializer.Deserialize(bytes));
    }
}
