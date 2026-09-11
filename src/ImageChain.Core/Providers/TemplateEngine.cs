using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ImageChain.Core.Providers;

public static partial class TemplateEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static string Resolve(Dictionary<string, object?> template, IDictionary<string, object?> parameters)
    {
        var json = JsonSerializer.Serialize(template, JsonOptions);
        var resolved = ReplacePlaceholders(json, parameters);
        return resolved;
    }

    public static string ResolveTemplate(string template, IDictionary<string, object?> parameters)
    {
        return ReplacePlaceholders(template, parameters);
    }

    private static string ReplacePlaceholders(string input, IDictionary<string, object?> parameters)
    {
        return PlaceholderRegex().Replace(input, match =>
        {
            var key = match.Groups[1].Value;
            if (parameters.TryGetValue(key, out var value))
            {
                return value switch
                {
                    string s => s,
                    JsonElement je => je.ValueKind switch
                    {
                        JsonValueKind.String => je.GetString() ?? "",
                        JsonValueKind.Number => je.GetRawText(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        JsonValueKind.Null => "null",
                        _ => je.GetRawText()
                    },
                    null => "null",
                    _ => value.ToString() ?? ""
                };
            }
            return match.Value;
        });
    }

    public static string ToBase64DataUri(byte[] imageBytes, string mediaType = "image/png")
    {
        return $"data:{mediaType};base64,{Convert.ToBase64String(imageBytes)}";
    }

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex PlaceholderRegex();
}
