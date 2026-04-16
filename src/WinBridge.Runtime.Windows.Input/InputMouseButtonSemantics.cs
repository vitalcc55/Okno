using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Input;

internal static class InputMouseButtonSemantics
{
    public const int VkLButton = 0x01;
    public const int VkRButton = 0x02;
    public const int VkMButton = 0x04;
    public const int VkXButton1 = 0x05;
    public const int VkXButton2 = 0x06;

    public const uint MouseEventfLeftDown = 0x0002;
    public const uint MouseEventfLeftUp = 0x0004;
    public const uint MouseEventfRightDown = 0x0008;
    public const uint MouseEventfRightUp = 0x0010;
    public const uint MouseEventfMiddleDown = 0x0020;
    public const uint MouseEventfMiddleUp = 0x0040;

    public static (uint DownFlag, uint UpFlag) GetDispatchFlags(string logicalButton, bool mouseButtonsSwapped) =>
        logicalButton switch
        {
            InputButtonValues.Left => mouseButtonsSwapped
                ? (MouseEventfRightDown, MouseEventfRightUp)
                : (MouseEventfLeftDown, MouseEventfLeftUp),
            InputButtonValues.Right => mouseButtonsSwapped
                ? (MouseEventfLeftDown, MouseEventfLeftUp)
                : (MouseEventfRightDown, MouseEventfRightUp),
            InputButtonValues.Middle => (MouseEventfMiddleDown, MouseEventfMiddleUp),
            _ => throw new ArgumentOutOfRangeException(nameof(logicalButton), logicalButton, null),
        };

    public static IReadOnlyList<string> GetActiveLogicalButtons(Func<int, short> getAsyncKeyState, bool mouseButtonsSwapped)
    {
        ArgumentNullException.ThrowIfNull(getAsyncKeyState);

        List<string> activeButtons = [];
        if (IsPressed(getAsyncKeyState, VkLButton))
        {
            activeButtons.Add(mouseButtonsSwapped ? "правая кнопка мыши" : "левая кнопка мыши");
        }

        if (IsPressed(getAsyncKeyState, VkRButton))
        {
            activeButtons.Add(mouseButtonsSwapped ? "левая кнопка мыши" : "правая кнопка мыши");
        }

        if (IsPressed(getAsyncKeyState, VkMButton))
        {
            activeButtons.Add("средняя кнопка мыши");
        }

        if (IsPressed(getAsyncKeyState, VkXButton1))
        {
            activeButtons.Add("кнопка мыши X1");
        }

        if (IsPressed(getAsyncKeyState, VkXButton2))
        {
            activeButtons.Add("кнопка мыши X2");
        }

        return activeButtons;
    }

    private static bool IsPressed(Func<int, short> getAsyncKeyState, int virtualKey) =>
        (getAsyncKeyState(virtualKey) & unchecked((short)0x8000)) != 0;
}
