namespace GLAS.Core.RegistryAccess;

public enum RegistryHive
{
    CurrentUser,
    LocalMachine,
}

/// <summary>
/// Minimal registry access surface, injectable for tests.
/// </summary>
public interface IRegistryService
{
    string? GetStringValue(RegistryHive hive, string keyPath, string valueName);

    /// <summary>Sub-key names under a key (empty when the key does not exist).</summary>
    string[] GetSubKeyNames(RegistryHive hive, string keyPath);

    /// <summary>Returns true when the value existed and was overwritten.</summary>
    bool SetStringValue(RegistryHive hive, string keyPath, string valueName, string value);

    /// <summary>Returns true when the value existed and was deleted.</summary>
    bool DeleteValue(RegistryHive hive, string keyPath, string valueName);
}
