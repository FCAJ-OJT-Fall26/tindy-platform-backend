using System.Globalization;
using Tindy.Application.Exceptions;

namespace Tindy.Application.DTOs.Ai;

public sealed record AiRoleReference(string Key, string Name);

public sealed class AiRoleReferenceMap
{
    private readonly IReadOnlyDictionary<string, Guid> _ids;
    public IReadOnlyList<AiRoleReference> References { get; }
    public IReadOnlySet<Guid> ActiveIds { get; }

    public AiRoleReferenceMap(IEnumerable<AiCatalogItem> catalog)
    {
        var items = catalog.OrderBy(item => item.Id).ToArray();
        if (items.Any(item => item.Id == Guid.Empty) || items.Select(item => item.Id).Distinct().Count() != items.Length)
            throw new ArgumentException("Role IDs must be nonempty and unique.", nameof(catalog));
        var ids = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var references = new List<AiRoleReference>();
        foreach (var (item, index) in items.Select((item, index) => (item, index)))
        {
            var key = "r" + (index + 1).ToString(CultureInfo.InvariantCulture);
            ids.Add(key, item.Id);
            references.Add(new(key, item.Name));
        }
        _ids = ids;
        References = references.AsReadOnly();
        ActiveIds = System.Collections.Frozen.FrozenSet.ToFrozenSet(ids.Values);
    }

    public Guid? Resolve(string? key)
    {
        if (key is null) return null;
        return _ids.TryGetValue(key, out var id) ? id : throw new AiException(AiFailure.InvalidOutput)
        { ValidationReason = "unknown_role_reference" };
    }
}
