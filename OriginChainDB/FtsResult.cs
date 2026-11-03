using System.Text.Json;

namespace OriginChainDB;

public sealed record FtsHit(string DocId, double Score, JsonElement? Highlights, JsonElement? Raw);

/// <summary>Uniform ranked, boolean and phrase results. Unranked hits have score zero.</summary>
public sealed class FtsResult
{
    public IReadOnlyList<FtsHit> Hits { get; }
    public JsonElement? Facets { get; }
    public JsonElement Raw { get; }
    internal FtsResult(JsonElement value)
    {
        Raw = value.Clone();
        var items = value;
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (!value.TryGetProperty("hits", out items)) throw new OriginChainException("protocol");
            Facets = value.TryGetProperty("facets", out var facets) ? facets.Clone() : null;
        }
        if (items.ValueKind != JsonValueKind.Array) throw new OriginChainException("protocol");
        Hits = items.EnumerateArray().Select(item =>
        {
            if (item.ValueKind == JsonValueKind.String) return new FtsHit(item.GetString()!, 0, null, null);
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("doc_id", out var id) || id.ValueKind != JsonValueKind.String)
                throw new OriginChainException("protocol");
            double score = 0;
            if (item.TryGetProperty("score", out var number) && (number.ValueKind != JsonValueKind.Number || !number.TryGetDouble(out score) || !double.IsFinite(score)))
                throw new OriginChainException("protocol");
            return new FtsHit(id.GetString()!, score, item.TryGetProperty("highlights", out var highlights) ? highlights.Clone() : null, item.Clone());
        }).ToArray();
    }
}
