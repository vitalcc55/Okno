// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;
using WinBridge.Setup.Core;

SetupCliArguments parser = SetupCliArguments.Parse(args);
if (!parser.IsValid)
{
    Console.Error.WriteLine(parser.ErrorMessage);
    return 1;
}

try
{
    ComputerUseWinRuntimeFoundationService runtimeService = new();
    ComputerUseWinInstallerService installerService = new(runtimeService);

    return (parser.CommandGroup, parser.CommandName) switch
    {
        ("runtime", "install") => WriteJsonOrText(runtimeService.InstallRuntime(parser.DescriptorPath), parser.JsonOutput),
        ("runtime", "status") => WriteRuntimeStatus(runtimeService.GetRuntimeStatus(parser.DescriptorPath), parser.JsonOutput, failOnInvalid: false),
        ("runtime", "verify") => WriteRuntimeStatus(runtimeService.VerifyRuntime(parser.DescriptorPath), parser.JsonOutput, failOnInvalid: true),
        ("runtime", "repair") => WriteJsonOrText(runtimeService.RepairRuntime(parser.DescriptorPath), parser.JsonOutput),

        ("install", "runtime-only") => WriteJsonOrText(installerService.InstallRuntimeOnly(parser.DescriptorPath), parser.JsonOutput),
        ("install", "codex") => WriteJsonOrText(installerService.InstallCodex(parser.DescriptorPath), parser.JsonOutput),
        ("update", "runtime-only") => WriteJsonOrText(installerService.UpdateRuntimeOnly(parser.DescriptorPath), parser.JsonOutput),
        ("update", "codex") => WriteJsonOrText(installerService.UpdateCodex(parser.DescriptorPath), parser.JsonOutput),
        ("repair", "runtime-only") => WriteJsonOrText(installerService.RepairRuntimeOnly(parser.DescriptorPath), parser.JsonOutput),
        ("repair", "codex") => WriteJsonOrText(installerService.RepairCodex(parser.DescriptorPath), parser.JsonOutput),
        ("uninstall", "runtime-only") => WriteJsonOrText(installerService.UninstallRuntimeOnly(), parser.JsonOutput),
        ("uninstall", "codex") => WriteJsonOrText(installerService.UninstallCodex(), parser.JsonOutput),
        ("status", null) => WriteJsonOrText(installerService.GetStatus(parser.DescriptorPath), parser.JsonOutput),

        _ => throw new InvalidOperationException($"Unsupported command '{parser.CommandGroup} {parser.CommandName}'."),
    };
}
catch (Exception ex)
{
    if (parser.JsonOutput)
    {
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            formatVersion = 1,
            error = ex.Message,
        }));
    }
    else
    {
        Console.Error.WriteLine(ex.Message);
    }

    return 1;
}

static int WriteJsonOrText<T>(T payload, bool jsonOutput)
{
    if (jsonOutput)
    {
        Console.WriteLine(JsonSerializer.Serialize(payload));
    }
    else
    {
        Console.WriteLine(payload);
    }

    return 0;
}

static int WriteRuntimeStatus(ComputerUseWinRuntimeStatus status, bool jsonOutput, bool failOnInvalid)
{
    if (jsonOutput)
    {
        Console.WriteLine(JsonSerializer.Serialize(status));
    }
    else
    {
        Console.WriteLine($"installed={status.IsInstalled}; usable={status.IsUsable}; compatible={status.IsCompatible}; runtimeRoot={status.EffectiveRuntimeRoot ?? "<none>"}");
    }

    if (!failOnInvalid)
    {
        return 0;
    }

    return status.IsInstalled && status.IsUsable && status.IsCompatible ? 0 : 1;
}

file sealed record SetupCliArguments(
    string CommandGroup,
    string? CommandName,
    string? DescriptorPath,
    bool JsonOutput,
    bool IsValid,
    string? ErrorMessage)
{
    public static SetupCliArguments Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return Invalid("Usage: runtime <install|status|verify|repair> [--descriptor-path <path>] [--json] OR <install|update|repair|uninstall> <codex|runtime-only> [--descriptor-path <path>] [--json] OR status [--descriptor-path <path>] [--json].");
        }

        string commandGroup = args[0];
        string? commandName = null;
        int optionStartIndex;

        if (commandGroup == "status")
        {
            optionStartIndex = 1;
        }
        else
        {
            if (args.Length < 2)
            {
                return Invalid($"Command group '{commandGroup}' requires a subcommand.");
            }

            commandName = args[1];
            optionStartIndex = 2;
        }

        string? descriptorPath = null;
        bool jsonOutput = false;

        for (int index = optionStartIndex; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--descriptor-path":
                    if (index + 1 >= args.Length)
                    {
                        return Invalid("The '--descriptor-path' option requires a value.");
                    }

                    descriptorPath = args[++index];
                    break;
                case "--json":
                    jsonOutput = true;
                    break;
                default:
                    return Invalid($"Unsupported argument '{args[index]}'.");
            }
        }

        return (commandGroup, commandName) switch
        {
            ("runtime", "install" or "status" or "verify" or "repair") => new SetupCliArguments(commandGroup, commandName, descriptorPath, jsonOutput, true, null),
            ("install", "codex" or "runtime-only") => new SetupCliArguments(commandGroup, commandName, descriptorPath, jsonOutput, true, null),
            ("update", "codex" or "runtime-only") => new SetupCliArguments(commandGroup, commandName, descriptorPath, jsonOutput, true, null),
            ("repair", "codex" or "runtime-only") => new SetupCliArguments(commandGroup, commandName, descriptorPath, jsonOutput, true, null),
            ("uninstall", "codex" or "runtime-only") => new SetupCliArguments(commandGroup, commandName, descriptorPath, jsonOutput, true, null),
            ("status", null) => new SetupCliArguments(commandGroup, commandName, descriptorPath, jsonOutput, true, null),
            _ => Invalid($"Unsupported command '{commandGroup} {commandName}'."),
        };
    }

    private static SetupCliArguments Invalid(string error)
    {
        return new SetupCliArguments(string.Empty, null, null, false, false, error);
    }
}
