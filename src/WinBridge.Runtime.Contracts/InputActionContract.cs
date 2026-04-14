namespace WinBridge.Runtime.Contracts;

[Flags]
public enum InputActionField
{
    None = 0,
    Type = 1 << 0,
    Point = 1 << 1,
    Path = 1 << 2,
    CoordinateSpace = 1 << 3,
    Button = 1 << 4,
    Keys = 1 << 5,
    Text = 1 << 6,
    Key = 1 << 7,
    Repeat = 1 << 8,
    Delta = 1 << 9,
    Direction = 1 << 10,
    CaptureReference = 1 << 11,
}

public sealed record InputActionContract(
    string ActionType,
    InputActionField RequiredFields,
    InputActionField AllowedFields);

public static class InputActionContractCatalog
{
    public static IReadOnlyList<InputActionContract> All { get; } =
    [
        new(
            InputActionTypeValues.Move,
            InputActionField.Type | InputActionField.Point | InputActionField.CoordinateSpace,
            InputActionField.Type | InputActionField.Point | InputActionField.CoordinateSpace | InputActionField.Keys | InputActionField.CaptureReference),
        new(
            InputActionTypeValues.Click,
            InputActionField.Type | InputActionField.Point | InputActionField.CoordinateSpace,
            InputActionField.Type | InputActionField.Point | InputActionField.CoordinateSpace | InputActionField.Button | InputActionField.Keys | InputActionField.CaptureReference),
        new(
            InputActionTypeValues.DoubleClick,
            InputActionField.Type | InputActionField.Point | InputActionField.CoordinateSpace,
            InputActionField.Type | InputActionField.Point | InputActionField.CoordinateSpace | InputActionField.Keys | InputActionField.CaptureReference),
        new(
            InputActionTypeValues.Drag,
            InputActionField.Type | InputActionField.Path | InputActionField.CoordinateSpace,
            InputActionField.Type | InputActionField.Path | InputActionField.CoordinateSpace | InputActionField.Keys | InputActionField.CaptureReference),
        new(
            InputActionTypeValues.Scroll,
            InputActionField.Type | InputActionField.Point | InputActionField.CoordinateSpace | InputActionField.Delta | InputActionField.Direction,
            InputActionField.Type | InputActionField.Point | InputActionField.CoordinateSpace | InputActionField.Keys | InputActionField.Delta | InputActionField.Direction | InputActionField.CaptureReference),
        new(
            InputActionTypeValues.Type,
            InputActionField.Type | InputActionField.Text,
            InputActionField.Type | InputActionField.Text),
        new(
            InputActionTypeValues.Keypress,
            InputActionField.Type | InputActionField.Key,
            InputActionField.Type | InputActionField.Key | InputActionField.Repeat),
    ];

    public static bool TryGet(string actionType, out InputActionContract contract)
    {
        foreach (InputActionContract item in All)
        {
            if (string.Equals(item.ActionType, actionType, StringComparison.Ordinal))
            {
                contract = item;
                return true;
            }
        }

        contract = null!;
        return false;
    }

    public static IReadOnlyList<InputActionField> EnumerateFields(InputActionField fields)
    {
        List<InputActionField> result = [];

        foreach (InputActionField field in OrderedFields)
        {
            if ((fields & field) == field)
            {
                result.Add(field);
            }
        }

        return result;
    }

    public static string GetJsonName(InputActionField field) =>
        field switch
        {
            InputActionField.Type => "type",
            InputActionField.Point => "point",
            InputActionField.Path => "path",
            InputActionField.CoordinateSpace => "coordinateSpace",
            InputActionField.Button => "button",
            InputActionField.Keys => "keys",
            InputActionField.Text => "text",
            InputActionField.Key => "key",
            InputActionField.Repeat => "repeat",
            InputActionField.Delta => "delta",
            InputActionField.Direction => "direction",
            InputActionField.CaptureReference => "captureReference",
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
        };

    private static IReadOnlyList<InputActionField> OrderedFields { get; } =
    [
        InputActionField.Type,
        InputActionField.Point,
        InputActionField.Path,
        InputActionField.CoordinateSpace,
        InputActionField.Button,
        InputActionField.Keys,
        InputActionField.Text,
        InputActionField.Key,
        InputActionField.Repeat,
        InputActionField.Delta,
        InputActionField.Direction,
        InputActionField.CaptureReference,
    ];
}
