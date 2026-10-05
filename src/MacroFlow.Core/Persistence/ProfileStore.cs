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
        Validate(profile);
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
        return await LoadFromPathAsync(path, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MacroProfile> LoadFromPathAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        if (new FileInfo(fullPath).Length > 5 * 1024 * 1024)
            throw new InvalidDataException("El perfil supera el límite de 5 MB.");
        var json = await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
        var profile = JsonSerializer.Deserialize<MacroProfile>(json, JsonOptions)
            ?? throw new InvalidDataException("El perfil no contiene datos válidos.");
        Validate(profile);
        return profile;
    }

    public async Task ExportAsync(MacroProfile profile, string path, CancellationToken cancellationToken = default)
    {
        Validate(profile);
        var fullPath = Path.GetFullPath(path);
        var temporaryPath = fullPath + ".tmp";
        var json = JsonSerializer.Serialize(profile, JsonOptions);
        await File.WriteAllTextAsync(temporaryPath, json, cancellationToken).ConfigureAwait(false);
        File.Move(temporaryPath, fullPath, overwrite: true);
    }

    private static void Validate(MacroProfile profile)
    {
        if (profile.FormatVersion is < 1 or > 1)
            throw new InvalidDataException($"Versión de perfil no compatible: {profile.FormatVersion}.");
        if (string.IsNullOrWhiteSpace(profile.Name))
            throw new InvalidDataException("El perfil no tiene nombre.");
        if (profile.Actions is null || profile.Actions.Count is 0 or > 10_000)
            throw new InvalidDataException("El perfil debe contener entre 1 y 10000 acciones.");
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
