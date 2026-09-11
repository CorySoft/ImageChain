using ImageChain.Core.Configuration;
using ImageChain.Core.Discovery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ImageChain.Web.Controllers;

[ApiController]
[Route("api/config")]
[Authorize(AuthenticationSchemes = "AdminPassword")]
public class ConfigController : ControllerBase
{
    private readonly ConfigStore _configStore;
    private readonly RuntimeConfig _runtime;
    private readonly IEnumerable<IVendorModelDiscovery> _discoveries;

    public ConfigController(ConfigStore configStore, RuntimeConfig runtime, IEnumerable<IVendorModelDiscovery> discoveries)
    {
        _configStore = configStore;
        _runtime = runtime;
        _discoveries = discoveries;
    }

    private async Task ReloadRuntimeAsync()
    {
        _runtime.Reload(await _configStore.LoadAsync());
    }

    [HttpGet("vendors")]
    public async Task<IActionResult> GetVendors()
    {
        var options = await _configStore.LoadAsync();
        return Ok(options.Vendors.Select(v => new
        {
            v.Name,
            v.BaseEndpoint,
            v.AuthType,
            ApiKey = MaskKey(v.ApiKey),
            v.Models
        }));
    }

    [HttpPost("vendors")]
    public async Task<IActionResult> AddVendor([FromBody] VendorConfig vendor)
    {
        var options = await _configStore.LoadAsync();
        var existing = options.Vendors.FirstOrDefault(v => v.Name.Equals(vendor.Name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            if (string.IsNullOrEmpty(vendor.ApiKey) || vendor.ApiKey.Contains('*'))
                vendor.ApiKey = existing.ApiKey;
            if (string.IsNullOrEmpty(vendor.BaseEndpoint))
                vendor.BaseEndpoint = existing.BaseEndpoint;
        }
        options.Vendors.RemoveAll(v => v.Name.Equals(vendor.Name, StringComparison.OrdinalIgnoreCase));
        options.Vendors.Add(vendor);
        await _configStore.SaveAsync(options);
        await ReloadRuntimeAsync();
        return Ok(new { message = $"已添加厂商 '{vendor.Name}'", version = _configStore.CurrentVersion });
    }

    [HttpDelete("vendors/{name}")]
    public async Task<IActionResult> DeleteVendor(string name)
    {
        var options = await _configStore.LoadAsync();
        options.Vendors.RemoveAll(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        await _configStore.SaveAsync(options);
        await ReloadRuntimeAsync();
        return Ok(new { message = $"已删除厂商 '{name}'", version = _configStore.CurrentVersion });
    }

    [HttpPost("vendors/{name}/discover")]
    public async Task<IActionResult> DiscoverModels(string name, [FromQuery] string? apiKey = null)
    {
        var options = await _configStore.LoadAsync();
        var vendor = options.Vendors.FirstOrDefault(v => v.Name == name);
        if (vendor is null) return NotFound(new { error = $"Vendor '{name}' not found" });

        var key = apiKey ?? vendor.ApiKey;
        var discovery = _discoveries.FirstOrDefault(d => d.VendorName == name)
                        ?? _discoveries.FirstOrDefault(d => d.VendorName == "OpenAI");

        if (discovery is null) return BadRequest(new { error = $"No discovery provider for '{name}'" });

        try
        {
            var models = await discovery.DiscoverModelsAsync(key, vendor.DiscoveryEndpoint ?? vendor.BaseEndpoint);
            return Ok(models);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("models")]
    public async Task<IActionResult> GetModels()
    {
        var options = await _configStore.LoadAsync();
        var allModels = options.Vendors
            .SelectMany(v => v.Models.Select(m => new
            {
                Vendor = v.Name,
                m.Id,
                m.DisplayName,
                m.Enabled,
                m.RetryCount,
                m.Capabilities,
                m.DefaultParameters,
                m.RequestTemplate,
                m.ResponseImagePath
            }))
            .OrderBy(m => m.Capabilities.Values.FirstOrDefault()?.Priority ?? 999);

        return Ok(allModels);
    }

    [HttpPut("models/update")]
    public async Task<IActionResult> UpdateModel([FromQuery] string vendor, [FromQuery] string modelId, [FromBody] ModelConfig model)
    {
        var options = await _configStore.LoadAsync();
        var vendorConfig = options.Vendors.FirstOrDefault(v => v.Name == vendor);
        if (vendorConfig is null) return NotFound(new { error = $"Vendor '{vendor}' not found" });

        var existing = vendorConfig.Models.FirstOrDefault(m => m.Id == modelId);
        if (existing is null) return NotFound(new { error = $"Model '{modelId}' not found" });

        model.Id = modelId;
        var index = vendorConfig.Models.IndexOf(existing);
        vendorConfig.Models[index] = model;

        await _configStore.SaveAsync(options);
        await ReloadRuntimeAsync();
        return Ok(new { message = $"模型 '{modelId}' 已更新", version = _configStore.CurrentVersion });
    }

    [HttpPut("models/reorder")]
    public async Task<IActionResult> ReorderModels([FromBody] List<ModelReorderItem> reorder)
    {
        var options = await _configStore.LoadAsync();
        var allModels = options.Vendors.SelectMany(v => v.Models.Select(m => (Vendor: v, Model: m))).ToList();

        foreach (var item in reorder)
        {
            var match = allModels.FirstOrDefault(m => m.Vendor.Name == item.Vendor && m.Model.Id == item.ModelId);
            if (match.Vendor is not null)
            {
                foreach (var cap in item.Capabilities)
                {
                    match.Model.Capabilities[cap.Key] = new CapabilityConfig { Priority = cap.Value };
                }
            }
        }

        await _configStore.SaveAsync(options);
        await ReloadRuntimeAsync();
        return Ok(new { message = "模型顺序已更新", version = _configStore.CurrentVersion });
    }

    [HttpGet("versions")]
    public IActionResult GetVersions()
    {
        return Ok(new
        {
            current = _configStore.CurrentVersion,
            available = _configStore.ListVersions()
        });
    }

    [HttpPost("rollback/{version}")]
    public async Task<IActionResult> Rollback(int version)
    {
        await _configStore.RollbackAsync(version);
        await ReloadRuntimeAsync();
        return Ok(new { message = $"已回滚到 v{version}", version = _configStore.CurrentVersion });
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export()
    {
        var options = await _configStore.LoadAsync();
        return Content(_configStore.ExportJson(options), "application/json");
    }

    [HttpPost("import")]
    public async Task<IActionResult> Import([FromBody] string json)
    {
        try
        {
            var options = await _configStore.ImportJsonAsync(json);
            await ReloadRuntimeAsync();
            return Ok(new { message = "已导入配置", version = _configStore.CurrentVersion });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("compositions")]
    public async Task<IActionResult> GetCompositions()
    {
        var options = await _configStore.LoadAsync();
        return Ok(new
        {
            active = options.ActiveComposition,
            compositions = options.Compositions.Select(c => new
            {
                c.Name,
                c.Description,
                Models = c.Models.Select(m => new { m.Vendor, m.ModelId }).ToList()
            }).ToList()
        });
    }

    [HttpPut("compositions")]
    public async Task<IActionResult> SaveComposition([FromBody] CompositionConfig composition)
    {
        if (string.IsNullOrWhiteSpace(composition.Name))
            return BadRequest(new { error = "组合名称不能为空" });

        var options = await _configStore.LoadAsync();
        options.Compositions.RemoveAll(c => c.Name.Equals(composition.Name, StringComparison.OrdinalIgnoreCase));
        options.Compositions.Add(composition);

        if (string.IsNullOrEmpty(options.ActiveComposition) && options.Compositions.Count == 1)
            options.ActiveComposition = composition.Name;

        await _configStore.SaveAsync(options);
        await ReloadRuntimeAsync();
        return Ok(new { message = $"组合 '{composition.Name}' 已保存", version = _configStore.CurrentVersion });
    }

    [HttpPost("compositions/active")]
    public async Task<IActionResult> ClearActiveComposition()
    {
        var options = await _configStore.LoadAsync();
        options.ActiveComposition = null;
        await _configStore.SaveAsync(options);
        await ReloadRuntimeAsync();
        return Ok(new { message = "已恢复为默认（全局顺序）", version = _configStore.CurrentVersion });
    }

    [HttpPost("compositions/{name}/active")]
    public async Task<IActionResult> SetActiveComposition(string name)
    {
        var options = await _configStore.LoadAsync();
        if (!options.Compositions.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            return NotFound(new { error = $"组合 '{name}' 不存在" });

        options.ActiveComposition = name;
        await _configStore.SaveAsync(options);
        await ReloadRuntimeAsync();
        return Ok(new { message = $"已切换到组合 '{name}'", version = _configStore.CurrentVersion });
    }

    [HttpDelete("compositions/{name}")]
    public async Task<IActionResult> DeleteComposition(string name)
    {
        var options = await _configStore.LoadAsync();
        options.Compositions.RemoveAll(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (string.Equals(options.ActiveComposition, name, StringComparison.OrdinalIgnoreCase))
            options.ActiveComposition = null;

        await _configStore.SaveAsync(options);
        await ReloadRuntimeAsync();
        return Ok(new { message = $"组合 '{name}' 已删除", version = _configStore.CurrentVersion });
    }

    private static string MaskKey(string key)
    {
        if (string.IsNullOrEmpty(key) || key.Length < 8)
            return "***";
        return $"{key[..4]}****{key[^4..]}";
    }
}

public sealed class ModelReorderItem
{
    public string Vendor { get; set; } = "";
    public string ModelId { get; set; } = "";
    public Dictionary<string, int> Capabilities { get; set; } = [];
}
