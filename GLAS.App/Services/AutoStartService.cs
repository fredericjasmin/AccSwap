using GLAS.Core.RegistryAccess;

namespace GLAS.App.Services;

/// <summary>
/// Windows autostart via HKCU\Software\Microsoft\Windows\CurrentVersion\Run.
/// </summary>
public static class AutoStartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "GLAS";

    public static bool IsEnabled(IRegistryService registry) =>
        registry.GetStringValue(RegistryHive.CurrentUser, RunKey, ValueName) is not null;

    public static void SetEnabled(IRegistryService registry, bool enabled)
    {
        if (enabled)
        {
            string exePath = Environment.ProcessPath ?? AppContext.BaseDirectory;
            registry.SetStringValue(RegistryHive.CurrentUser, RunKey, ValueName, $"\"{exePath}\"");
        }
        else
        {
            registry.DeleteValue(RegistryHive.CurrentUser, RunKey, ValueName);
        }
    }
}
