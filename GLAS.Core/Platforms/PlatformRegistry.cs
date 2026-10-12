namespace GLAS.Core.Platforms;

/// <summary>
/// Holds the known platform instances; adding a launcher means adding one
/// entry here.
/// </summary>
public sealed class PlatformRegistry
{
    private readonly List<IPlatform> _platforms = [];

    public IReadOnlyList<IPlatform> Platforms => _platforms;

    public void Register(IPlatform platform) => _platforms.Add(platform);

    public void Clear() => _platforms.Clear();

    public IPlatform? Find(string platformId) =>
        _platforms.FirstOrDefault(p => string.Equals(p.Id, platformId, StringComparison.OrdinalIgnoreCase));
}
