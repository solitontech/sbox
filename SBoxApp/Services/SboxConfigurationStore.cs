using System.Text.Json;
using Microsoft.Maui.Storage;
using SBoxApp.Models;

namespace SBoxApp.Services;

public class SboxConfigurationStore
{
    private const string FileName = "settings.json";
    private readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web);
    private readonly string _configDirectory;
    private readonly string _configPath;
    private SboxConfiguration _current;

    public SboxConfigurationStore()
    {
        _configDirectory = ResolveConfigDirectory();
        _configPath = Path.Combine(_configDirectory, FileName);
        _current = LoadFromDisk();
    }

    public SboxConfiguration GetSnapshot() => _current.Clone();

    public async Task SaveAsync(SboxConfiguration configuration)
    {
        _current = configuration.Clone();
        try
        {
            Directory.CreateDirectory(_configDirectory);
            await using var stream = File.Create(_configPath);
            await JsonSerializer.SerializeAsync(stream, _current, _serializerOptions);
        }
        catch
        {
            // ignored - caller experiences last-known state
        }
    }

    private SboxConfiguration LoadFromDisk()
    {
        try
        {
            if (!File.Exists(_configPath))
            {
                return new SboxConfiguration();
            }

            using var stream = File.OpenRead(_configPath);
            var model = JsonSerializer.Deserialize<SboxConfiguration>(stream, _serializerOptions);
            return model ?? new SboxConfiguration();
        }
        catch
        {
            return new SboxConfiguration();
        }
    }

    private static string ResolveConfigDirectory()
    {
        var basePath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(basePath))
        {
            basePath = FileSystem.AppDataDirectory;
        }

        return Path.Combine(basePath, "Soliton", "SBox");
    }
}
