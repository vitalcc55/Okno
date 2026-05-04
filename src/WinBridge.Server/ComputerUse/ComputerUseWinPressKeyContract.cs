// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;

namespace WinBridge.Server.ComputerUse;

internal sealed record ComputerUseWinPressKeyLiteral(
    string Normalized,
    string KeyCategory,
    string BaseKey,
    IReadOnlyList<string> Modifiers);

internal static class ComputerUseWinPressKeyContract
{
    private static readonly HashSet<string> ModifierTokens =
        new(StringComparer.Ordinal)
        {
            "ctrl",
            "alt",
            "shift",
            "win",
        };

    private static readonly HashSet<string> NamedBaseTokens =
        new(StringComparer.Ordinal)
        {
            "tab",
            "enter",
            "escape",
            "delete",
            "backspace",
            "space",
            "up",
            "down",
            "left",
            "right",
            "home",
            "end",
            "page_up",
            "page_down",
            "insert",
            "f1",
            "f2",
            "f3",
            "f4",
            "f5",
            "f6",
            "f7",
            "f8",
            "f9",
            "f10",
            "f11",
            "f12",
        };

    public static string? ValidateRequest(ComputerUseWinPressKeyRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.StateToken))
        {
            return "Параметр stateToken обязателен для press_key.";
        }

        if (request.Repeat is not null
            && (request.Repeat.Value < InputActionScalarConstraints.MinimumRepeat
                || request.Repeat.Value > InputActionScalarConstraints.MaximumKeypressRepeat))
        {
            return $"Параметр repeat для press_key должен быть в диапазоне {InputActionScalarConstraints.MinimumRepeat}..{InputActionScalarConstraints.MaximumKeypressRepeat}.";
        }

        if (!TryParse(request.Key, out _, out string? failure))
        {
            return failure;
        }

        return null;
    }

    public static bool TryParse(
        string? keyLiteral,
        out ComputerUseWinPressKeyLiteral? literal,
        out string? failure)
    {
        literal = null;

        if (string.IsNullOrWhiteSpace(keyLiteral))
        {
            failure = "Параметр key обязателен для press_key.";
            return false;
        }

        string[] rawTokens = keyLiteral
            .Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeToken)
            .ToArray();
        if (rawTokens.Length == 0 || rawTokens.Length != keyLiteral.Split('+').Length)
        {
            failure = "Параметр key для press_key должен использовать named key literal или modifier combo без пустых сегментов.";
            return false;
        }

        string baseToken = rawTokens[^1];
        string[] modifiers = rawTokens[..^1];

        if (ModifierTokens.Contains(baseToken))
        {
            failure = "Последний сегмент key для press_key должен быть named key или shortcut key, а не modifier.";
            return false;
        }

        if (modifiers.Length != modifiers.Distinct(StringComparer.Ordinal).Count()
            || modifiers.Any(token => !ModifierTokens.Contains(token)))
        {
            failure = "Modifier combo для press_key поддерживает только уникальные `ctrl`, `alt`, `shift`, `win` перед base key.";
            return false;
        }

        bool isShortcutKey = baseToken.Length == 1 && char.IsLetterOrDigit(baseToken[0]);
        bool isNamedBaseKey = NamedBaseTokens.Contains(baseToken);
        if (!isShortcutKey && !isNamedBaseKey)
        {
            failure = $"Неподдерживаемый key literal '{keyLiteral}' для press_key.";
            return false;
        }

        if (isShortcutKey && modifiers.Length == 0)
        {
            failure = "Bare printable key для press_key не поддерживается; используй named key или modifier combo, а для текста - type_text.";
            return false;
        }

        literal = new(
            string.Join("+", modifiers.Append(baseToken)),
            modifiers.Length == 0 ? "named_key" : "combo",
            baseToken,
            modifiers);
        failure = null;
        return true;
    }

    public static bool RequiresConfirmation(ComputerUseWinPressKeyLiteral literal)
    {
        ArgumentNullException.ThrowIfNull(literal);

        return literal.BaseKey switch
        {
            "enter" => true,
            "delete" => true,
            "f4" => literal.Modifiers.Contains("alt", StringComparer.Ordinal),
            "w" => literal.Modifiers.Contains("ctrl", StringComparer.Ordinal),
            "q" => literal.Modifiers.Contains("ctrl", StringComparer.Ordinal),
            "l" => literal.Modifiers.Contains("win", StringComparer.Ordinal),
            _ => false,
        };
    }

    private static string NormalizeToken(string token)
    {
        string normalized = token.Trim().ToLowerInvariant();
        return normalized switch
        {
            "control" => "ctrl",
            "esc" => "escape",
            "return" => "enter",
            "arrow_up" => "up",
            "arrow_down" => "down",
            "arrow_left" => "left",
            "arrow_right" => "right",
            "pageup" => "page_up",
            "pagedown" => "page_down",
            _ => normalized,
        };
    }
}
