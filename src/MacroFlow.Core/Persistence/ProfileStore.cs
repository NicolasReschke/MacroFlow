using System.Text.Json;
using System.Text.Json.Serialization;
using MacroFlow.Core.Models;

namespace MacroFlow.Core.Persistence;

public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public ProfileStore(string directory)
    {
        DirectoryPath = Path.GetFullPath(directory);
    }

    public string DirectoryPath { get; }

    public IReadOnlyList<string> ListProfiles()
    {
        if (!Directory.Exists(DirectoryPath))
        {
            return [];
        }

        return Directory.EnumerateFiles(DirectoryPath, "*.json", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task SaveAsync(MacroProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Directory.CreateDirectory(DirectoryPath);
        var path = GetSafePath(profile.Name);
        var temporaryPath = path + ".tmp";
        var json = JsonSerializer.Serialize(profile, JsonOptions);
        await File.WriteAllTextAsync(temporaryPath, json, cancellationToken).ConfigureAwait(false);
        File.Move(temporaryPath, path, overwrite: true);
    }

    public async Task<MacroProfile> LoadAsync(string name, CancellationToken cancellationToken = default)
    {
        var path = GetSafePath(name);
        var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<MacroProfile>(json, JsonOptions)
            ?? throw new InvalidDataException("El perfil no contiene datos válidos.");
    }

    private string GetSafePath(string name)
    {
        var safeName = string.Concat((name ?? string.Empty).Trim().Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));

        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = "perfil";
        }

        return Path.Combine(DirectoryPath, safeName + ".json");
    }
}
