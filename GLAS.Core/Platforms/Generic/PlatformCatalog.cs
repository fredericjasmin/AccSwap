using System.Reflection;
using System.Resources;
using System.Text.Json.Serialization;
using System.Text.Json;

namespace GLAS.Core.Platforms.Generic;

/// <summary>
/// Loads platform definitions: a built-in catalog shipped as an embedded
/// resource, optionally overridden/extended by %APPDATA%\GLAS\platforms.json
/// (entries with the same id replace the built-in ones).
/// </summary>
public static class PlatformCatalog
{
    private const string ResourceName = "GLAS.Core.Platforms.Generic.platforms.json";
    private const string UserFileName = "platforms.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static List<PlatformDefinition> Load(string glasRootDir)
    {
        List<PlatformDefinition> platforms = LoadEmbedded();

        string userFile = Path.Combine(glasRootDir, UserFileName);
        if (File.Exists(userFile))
        {
            try
            {
                List<PlatformDefinition>? user = JsonSerializer.Deserialize<List<PlatformDefinition>>(
                    File.ReadAllText(userFile), JsonOptions);
                if (user is not null)
                {
                    foreach (PlatformDefinition definition in user)
                    {
                        definition.Validate();
                        int existing = platforms.FindIndex(p => p.Id.Equals(definition.Id, StringComparison.OrdinalIgnoreCase));
                        if (existing >= 0)
                        {
                            platforms[existing] = definition;
                        }
                        else
                        {
                            platforms.Add(definition);
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // A broken user file falls back to the built-in catalog.
            }
        }

        return platforms;
    }

    private static List<PlatformDefinition> LoadEmbedded()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        using Stream? stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new MissingManifestResourceException($"Embedded catalog not found: {ResourceName}");
        var platforms = JsonSerializer.Deserialize<List<PlatformDefinition>>(stream, JsonOptions)
            ?? throw new InvalidDataException("Embedded platform catalog is empty.");
        foreach (PlatformDefinition definition in platforms)
        {
            definition.Validate();
        }

        return platforms;
    }
}
