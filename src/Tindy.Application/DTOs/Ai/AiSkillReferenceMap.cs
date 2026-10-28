using System.Collections.ObjectModel;
using System.Globalization;
using Tindy.Application.Exceptions;

namespace Tindy.Application.DTOs.Ai;

public sealed record AiSkillReference(string Key, string Name);

// The immutable snapshot belongs to one extraction request, never to a global cache.
public sealed class AiSkillReferenceMap
{
    private readonly IReadOnlyDictionary<string, Guid> _ids;
    public IReadOnlyList<AiSkillReference> References { get; }

    public AiSkillReferenceMap(IEnumerable<AiCatalogItem> catalog)
    {
        var items = catalog.OrderBy(item => item.Id).ToArray();
        if (items.Any(item => item.Id == Guid.Empty) || items.Select(item => item.Id).Distinct().Count() != items.Length)
            throw new ArgumentException("Catalog IDs must be nonempty and unique.", nameof(catalog));
        var ids = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var references = new List<AiSkillReference>();
        for (var index = 0; index < items.Length; index++)
        {
            var key = "s" + (index + 1).ToString(CultureInfo.InvariantCulture);
            ids.Add(key, items[index].Id);
            references.Add(new AiSkillReference(key, items[index].Name));
        }
        _ids = new ReadOnlyDictionary<string, Guid>(ids);
        References = references.AsReadOnly();
    }

    public IReadOnlyList<Guid> Resolve(IReadOnlyList<string>? keys, int maximum)
    {
        if (keys is null) throw new AiException(AiFailure.InvalidOutput) { ValidationReason = "invalid_skill_array" };
        if (keys.Count > maximum) throw new AiException(AiFailure.InvalidOutput) { ValidationReason = "list_limit_exceeded" };
        if (keys.Distinct(StringComparer.Ordinal).Count() != keys.Count)
            throw new AiException(AiFailure.InvalidOutput) { ValidationReason = "duplicate_skill_reference" };
        if (keys.Any(key => key is null || !_ids.ContainsKey(key)))
            throw new AiException(AiFailure.InvalidOutput) { ValidationReason = "unknown_skill_reference" };
        return keys.Select(key => _ids[key]).ToArray();
    }
}
