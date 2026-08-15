using LTS.Application.Integration;

namespace LTS.Infrastructure.Integration;

/// <summary>
/// Resolves adapters by the key stored on the integration source, so which system a country
/// talks to is configuration rather than a branch in the code.
/// </summary>
public sealed class IntegrationAdapterRegistry(IEnumerable<ICountryIntegrationAdapter> adapters)
    : IIntegrationAdapterRegistry
{
    private readonly Dictionary<string, ICountryIntegrationAdapter> _adapters =
        adapters.ToDictionary(a => a.AdapterKey, StringComparer.OrdinalIgnoreCase);

    public ICountryIntegrationAdapter? Find(string adapterKey) =>
        string.IsNullOrWhiteSpace(adapterKey) ? null : _adapters.GetValueOrDefault(adapterKey);

    public IReadOnlyList<string> RegisteredKeys => [.. _adapters.Keys.Order()];
}
