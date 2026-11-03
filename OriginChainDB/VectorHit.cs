using System.Text.Json;

namespace OriginChainDB;

/// <summary>A vector match with a stable ID field and retained server metadata.</summary>
public sealed record VectorHit(string Id, double Score, JsonElement? Metadata, JsonElement Raw)
{
    internal static IReadOnlyList<VectorHit> Decode(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array) throw new OriginChainException("protocol");
        return value.EnumerateArray().Select(item =>
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !(item.TryGetProperty("id", out var id) || item.TryGetProperty("vec_id", out id)) || id.ValueKind != JsonValueKind.String ||
                !item.TryGetProperty("score", out var score) || score.ValueKind != JsonValueKind.Number || !score.TryGetDouble(out var number) || !double.IsFinite(number))
                throw new OriginChainException("protocol");
            return new VectorHit(id.GetString()!, number, item.TryGetProperty("metadata", out var metadata) ? metadata.Clone() : null, item.Clone());
        }).ToArray();
    }
}
