namespace AccSwap.Core.Steam.Vdf;

/// <summary>
/// A value in a VDF document: either a quoted string or a nested node.
/// </summary>
public abstract record VdfValue
{
    public static VdfValue From(string text) => new VdfString(text);

    public static VdfValue From(VdfNode node) => node;
}

public sealed record VdfString(string Value) : VdfValue;

public sealed record VdfNode : VdfValue
{
    private readonly List<VdfEntry> _entries = [];

    public IReadOnlyList<VdfEntry> Entries => _entries;

    public void Add(string key, VdfValue value) => _entries.Add(new VdfEntry(key, value));

    public VdfNode AddNode(string key)
    {
        var node = new VdfNode();
        _entries.Add(new VdfEntry(key, node));
        return node;
    }

    public VdfEntry? FirstOrNull(string key) =>
        _entries.FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.Ordinal));

    public string? GetString(string key) => (FirstOrNull(key)?.Value as VdfString)?.Value;

    public VdfNode? GetNode(string key) => FirstOrNull(key)?.Value as VdfNode;

    /// <summary>Replaces the first entry with this key, or appends a new one.</summary>
    public void Set(string key, string value)
    {
        int index = _entries.FindIndex(e => string.Equals(e.Key, key, StringComparison.Ordinal));
        var entry = new VdfEntry(key, new VdfString(value));
        if (index >= 0)
        {
            _entries[index] = entry;
        }
        else
        {
            _entries.Add(entry);
        }
    }
}

public sealed record VdfEntry(string Key, VdfValue Value);
