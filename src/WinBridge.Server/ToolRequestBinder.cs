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
        out string? reason,
        Func<T, string?>? validator = null)
    {
        if (arguments is null)
        {
            request = fallbackRequest;
            reason = validator?.Invoke(request);
            return reason is null;
        }

        try
        {
            JsonElement rawArguments = JsonSerializer.SerializeToElement(arguments, BindingJsonOptions);
            request = rawArguments.Deserialize<T>(BindingJsonOptions)
                ?? throw new JsonException($"Transport arguments did not deserialize to {typeof(T).Name}.");
            reason = validator?.Invoke(request);
            if (reason is not null)
            {
                request = fallbackRequest;
                return false;
            }

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
