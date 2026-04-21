using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinBridge.Server;

internal static class ToolRequestBinder
{
    private static readonly JsonSerializerOptions BindingJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static bool TryBind<T>(
        IDictionary<string, JsonElement>? arguments,
        T fallbackRequest,
        out T request,
        out string? reason)
    {
        if (arguments is null)
        {
            request = fallbackRequest;
            reason = null;
            return true;
        }

        try
        {
            JsonElement rawArguments = JsonSerializer.SerializeToElement(arguments, BindingJsonOptions);
            request = rawArguments.Deserialize<T>(BindingJsonOptions)
                ?? throw new JsonException($"Transport arguments did not deserialize to {typeof(T).Name}.");
            reason = null;
            return true;
        }
        catch (JsonException exception)
        {
            request = fallbackRequest;
            reason = exception.Message;
            return false;
        }
    }
}
