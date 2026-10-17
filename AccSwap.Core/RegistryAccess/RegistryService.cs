using Microsoft.Win32;

namespace AccSwap.Core.RegistryAccess;

public sealed class RegistryService : IRegistryService
{
    public string? GetStringValue(RegistryHive hive, string keyPath, string valueName)
    {
        using RegistryKey? baseKey = OpenBaseKey(hive, writable: false);
        using RegistryKey? key = baseKey?.OpenSubKey(keyPath);
        return key?.GetValue(valueName) as string;
    }

    public string[] GetSubKeyNames(RegistryHive hive, string keyPath)
    {
        using RegistryKey? baseKey = OpenBaseKey(hive, writable: false);
        using RegistryKey? key = baseKey?.OpenSubKey(keyPath);
        return key?.GetSubKeyNames() ?? [];
    }

    public bool SetStringValue(RegistryHive hive, string keyPath, string valueName, string value)
    {
        using RegistryKey? baseKey = OpenBaseKey(hive, writable: true);
        using RegistryKey? key = baseKey?.CreateSubKey(keyPath, writable: true);
        if (key is null)
        {
            throw new InvalidOperationException($"Cannot open registry key for writing: {keyPath}");
        }

        bool existed = key.GetValue(valueName) is not null;
        key.SetValue(valueName, value, RegistryValueKind.String);
        return existed;
    }

    public bool DeleteValue(RegistryHive hive, string keyPath, string valueName)
    {
        using RegistryKey? baseKey = OpenBaseKey(hive, writable: true);
        using RegistryKey? key = baseKey?.OpenSubKey(keyPath, writable: true);
        if (key?.GetValue(valueName) is null)
        {
            return false;
        }

        key.DeleteValue(valueName);
        return true;
    }

    private static RegistryKey? OpenBaseKey(RegistryHive hive, bool writable) => hive switch
    {
        RegistryHive.CurrentUser => Registry.CurrentUser,
        RegistryHive.LocalMachine => Registry.LocalMachine,
        _ => null,
    };
}
