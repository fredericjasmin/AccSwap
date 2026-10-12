namespace GLAS.Core.Platforms.Generic;

public enum LoginTargetKind
{
    /// <summary>A single file (env-expanded path).</summary>
    File,

    /// <summary>Files matching a wildcard pattern, e.g. C:\dir\Cache\data_*.</summary>
    Glob,

    /// <summary>A whole directory tree.</summary>
    Directory,

    /// <summary>String values under one HKCU key (valueName may contain *).</summary>
    Registry,

    /// <summary>A string field inside a JSON file (dotted path).</summary>
    JsonField,
}

public enum UniqueIdKind
{
    /// <summary>SHA-256 of the file content.</summary>
    FileHash,

    /// <summary>First regex match inside the file (e.g. e-mail).</summary>
    FileRegex,

    /// <summary>SHA-256 of the directory listing (names + sizes).</summary>
    DirHash,

    /// <summary>A HKCU string value.</summary>
    RegistryValue,

    /// <summary>A string field inside a JSON file (dotted path).</summary>
    JsonField,
}

/// <summary>Declarative description of one switchable platform.</summary>
public sealed class PlatformDefinition
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public List<string> ExeCandidates { get; set; } = [];

    /// <summary>Start Menu shortcut name patterns (regex, e.g. "Epic Games Launcher").</summary>
    public List<string> ShortcutNames { get; set; } = [];

    /// <summary>Windows uninstall-registry fallback for custom install drives.</summary>
    public UninstallInfo? Uninstall { get; set; }

    public string LaunchArguments { get; set; } = "";

    public List<string> ProcessesToClose { get; set; } = [];

    public List<LoginTarget> Targets { get; set; } = [];

    public UniqueIdDefinition? UniqueId { get; set; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            throw new InvalidDataException("Platform definition is missing 'id'.");
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new InvalidDataException($"Platform '{Id}' is missing 'name'.");
        }

        if (ExeCandidates.Count == 0)
        {
            throw new InvalidDataException($"Platform '{Id}' has no exeCandidates.");
        }

        if (ProcessesToClose.Count == 0)
        {
            throw new InvalidDataException($"Platform '{Id}' has no processesToClose.");
        }
    }
}

public sealed class UninstallInfo
{
    /// <summary>Regex matched against the uninstall entry's DisplayName.</summary>
    public string DisplayNamePattern { get; set; } = "";

    /// <summary>Exe path relative to the entry's InstallLocation (optional).</summary>
    public string? RelativeExe { get; set; }
}

public sealed class LoginTarget
{
    public LoginTargetKind Type { get; set; }

    /// <summary>Env-expanded path; may contain * for Glob targets.</summary>
    public string Path { get; set; } = "";

    /// <summary>Registry key path (HKCU-relative), for Registry targets.</summary>
    public string? KeyPath { get; set; }

    /// <summary>Registry value name; may contain * for Registry targets.</summary>
    public string? ValueName { get; set; }

    /// <summary>Dotted field path, for JsonField targets.</summary>
    public string? JsonPath { get; set; }
}

public sealed class UniqueIdDefinition
{
    public UniqueIdKind Kind { get; set; }

    public string Path { get; set; } = "";

    public string? Regex { get; set; }

    public string? KeyPath { get; set; }

    public string? ValueName { get; set; }

    public string? JsonPath { get; set; }
}
