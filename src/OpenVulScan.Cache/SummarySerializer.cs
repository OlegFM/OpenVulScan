using System.Collections.Immutable;
using MessagePack;
using MessagePack.Resolvers;

namespace OpenVulScan;

/// <summary>
/// Persists <see cref="MethodSummary"/> batches as MessagePack (LZ4-compressed) inside a
/// versioned envelope. Format contract lives in <c>docs/cache-format.md</c>; readers reject
/// any envelope whose <c>FormatVersion</c> differs from the one they were built for.
/// </summary>
public static class SummarySerializer
{
    internal const int CurrentFormatVersion = 1;

    private static readonly MessagePackSerializerOptions s_options =
        MessagePackSerializerOptions.Standard
            .WithResolver(StandardResolverAllowPrivate.Instance)
            .WithCompression(MessagePackCompression.Lz4BlockArray);

    public static byte[] Serialize(IReadOnlyList<MethodSummary> summaries)
        => SerializeWithVersion(summaries, CurrentFormatVersion);

    internal static byte[] SerializeWithVersion(IReadOnlyList<MethodSummary> summaries, int formatVersion)
    {
        ArgumentNullException.ThrowIfNull(summaries);

        var envelope = new SummaryFileDto
        {
            FormatVersion = formatVersion,
            Summaries = [.. summaries.Select(ToDto)],
        };

        return MessagePackSerializer.Serialize(envelope, s_options);
    }

    public static ImmutableArray<MethodSummary> Deserialize(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var envelope = MessagePackSerializer.Deserialize<SummaryFileDto>(bytes, s_options);
        if (envelope.FormatVersion != CurrentFormatVersion)
        {
            throw new InvalidDataException(
                $"Summary cache format version {envelope.FormatVersion} is not supported; this reader expects {CurrentFormatVersion}.");
        }

        return [.. envelope.Summaries.Select(FromDto)];
    }

    private static MethodSummaryDto ToDto(MethodSummary summary) => new()
    {
        MethodId = summary.MethodId,
        ReturnNullability = (byte)summary.ReturnNullability,
        OutParameters = [.. summary.OutParameters.Select(
            p => new ParameterNullabilityDto { Position = p.Position, State = (byte)p.State })],
        Throws = [.. summary.Throws],
        IsPure = summary.IsPure,
        TaintPassThrough = [.. summary.TaintPassThrough],
    };

    private static MethodSummary FromDto(MethodSummaryDto dto) => new(
        dto.MethodId,
        (NullState)dto.ReturnNullability,
        [.. dto.OutParameters.Select(p => new ParameterNullability(p.Position, (NullState)p.State))],
        [.. dto.Throws],
        dto.IsPure,
        [.. dto.TaintPassThrough]);

    [MessagePackObject(AllowPrivate = true)]
    internal sealed class SummaryFileDto
    {
        [Key(0)]
        public int FormatVersion { get; set; }

        [Key(1)]
        public MethodSummaryDto[] Summaries { get; set; } = [];
    }

    [MessagePackObject(AllowPrivate = true)]
    internal sealed class MethodSummaryDto
    {
        [Key(0)]
        public string MethodId { get; set; } = string.Empty;

        [Key(1)]
        public byte ReturnNullability { get; set; }

        [Key(2)]
        public ParameterNullabilityDto[] OutParameters { get; set; } = [];

        [Key(3)]
        public string[] Throws { get; set; } = [];

        [Key(4)]
        public bool IsPure { get; set; }

        [Key(5)]
        public int[] TaintPassThrough { get; set; } = [];
    }

    [MessagePackObject(AllowPrivate = true)]
    internal sealed class ParameterNullabilityDto
    {
        [Key(0)]
        public int Position { get; set; }

        [Key(1)]
        public byte State { get; set; }
    }
}
