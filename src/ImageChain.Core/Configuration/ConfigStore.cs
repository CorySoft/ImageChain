using System.Text.Json;
using ImageChain.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ImageChain.Core.Configuration;

public sealed class ConfigStore
{
    private readonly string _configPath;
    private readonly string _backupDir;
    private readonly ILogger<ConfigStore> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private int _version;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ConfigStore(string configPath, ILogger<ConfigStore> logger)
    {
        _configPath = Path.GetFullPath(configPath);
        _backupDir = Path.Combine(Path.GetDirectoryName(_configPath)!, "config-backups");
        _logger = logger;

        Directory.CreateDirectory(_backupDir);

        if (File.Exists(_configPath))
        {
            var json = File.ReadAllText(_configPath);
            var options = JsonSerializer.Deserialize<ImageChainOptions>(json, JsonOptions);
            _version = options?.ConfigVersion ?? 0;
        }
    }

    public int CurrentVersion => _version;

    public void EnsureSeeded(ImageChainOptions seed)
    {
        if (!File.Exists(_configPath))
        {
            var json = JsonSerializer.Serialize(seed, JsonOptions);
            File.WriteAllText(_configPath, json);
            _version = seed.ConfigVersion;
            _logger.LogInformation("已初始化配置到 {Path}", _configPath);
            return;
        }

        var current = LoadAsync().GetAwaiter().GetResult();
        var changed = false;

        if (string.IsNullOrEmpty(current.AdminPassword) && !string.IsNullOrEmpty(seed.AdminPassword))
        {
            current.AdminPassword = seed.AdminPassword;
            changed = true;
        }

        if (string.IsNullOrEmpty(current.ApiKey) && !string.IsNullOrEmpty(seed.ApiKey))
        {
            current.ApiKey = seed.ApiKey;
            changed = true;
        }

        foreach (var seedVendor in seed.Vendors)
        {
            if (current.Vendors.Any(v => v.Name.Equals(seedVendor.Name, StringComparison.OrdinalIgnoreCase)))
                continue;
            current.Vendors.Add(seedVendor);
            changed = true;
        }

        if (changed)
        {
            SaveAsync(current).GetAwaiter().GetResult();
            _logger.LogInformation("已将种子配置合并进 {Path} (v{Version})", _configPath, _version);
        }
    }

    public async Task<ImageChainOptions> LoadAsync()
    {
        if (!File.Exists(_configPath))
        {
            _logger.LogWarning("Config file not found at {Path}, using defaults", _configPath);
            return new ImageChainOptions();
        }

        var json = await File.ReadAllTextAsync(_configPath);
        var options = JsonSerializer.Deserialize<ImageChainOptions>(json, JsonOptions)
                      ?? new ImageChainOptions();

        _version = options.ConfigVersion;
        _logger.LogInformation("Loaded config v{Version} from {Path}", _version, _configPath);
        return options;
    }

    public async Task SaveAsync(ImageChainOptions options)
    {
        await _lock.WaitAsync();
        try
        {
            var backupPath = Path.Combine(_backupDir, $"config-v{_version}.json");

            if (File.Exists(_configPath))
            {
                File.Copy(_configPath, backupPath, overwrite: true);
                _logger.LogDebug("Backed up config v{Version} to {Backup}", _version, backupPath);
            }

            options.ConfigVersion = ++_version;

            var json = JsonSerializer.Serialize(options, JsonOptions);
            var tempPath = _configPath + ".tmp";
            await File.WriteAllTextAsync(tempPath, json);

            File.Move(tempPath, _configPath, overwrite: true);
            _logger.LogInformation("Saved config v{Version} atomically to {Path}", _version, _configPath);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RollbackAsync(int targetVersion)
    {
        var backupPath = Path.Combine(_backupDir, $"config-v{targetVersion}.json");
        if (!File.Exists(backupPath))
        {
            throw new FileNotFoundException($"Backup for version {targetVersion} not found", backupPath);
        }

        await _lock.WaitAsync();
        try
        {
            File.Copy(backupPath, _configPath, overwrite: true);
            _version = targetVersion;
            _logger.LogInformation("Rolled back config to v{Version}", targetVersion);
        }
        finally
        {
            _lock.Release();
        }
    }

    public IReadOnlyList<int> ListVersions()
    {
        return Directory.GetFiles(_backupDir, "config-v*.json")
            .Select(f => int.Parse(Path.GetFileNameWithoutExtension(f)
                .Replace("config-v", "")))
            .OrderByDescending(v => v)
            .ToList();
    }

    public string ExportJson(ImageChainOptions options)
    {
        return JsonSerializer.Serialize(options, JsonOptions);
    }

    public async Task<ImageChainOptions> ImportJsonAsync(string json)
    {
        var options = JsonSerializer.Deserialize<ImageChainOptions>(json, JsonOptions)
                      ?? throw new InvalidOperationException("Invalid configuration JSON");
        await SaveAsync(options);
        return options;
    }
}
