// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;

namespace WinBridge.Server.ComputerUse;

internal sealed class ComputerUseWinApprovalStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly object gate = new();
    private readonly string storePath;
    private HashSet<string>? approvedApps;

    public ComputerUseWinApprovalStore(ComputerUseWinOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        storePath = options.ApprovalStorePath;
    }

    public bool IsApproved(string appId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        lock (gate)
        {
            EnsureLoaded();
            return approvedApps!.Contains(Normalize(appId));
        }
    }

    public void Approve(string appId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        lock (gate)
        {
            EnsureLoaded();
            if (!approvedApps!.Add(Normalize(appId)))
            {
                return;
            }

            string directory = Path.GetDirectoryName(storePath)
                ?? throw new InvalidOperationException("Approval store path does not have a parent directory.");
            Directory.CreateDirectory(directory);
            string document = JsonSerializer.Serialize(approvedApps.OrderBy(static item => item, StringComparer.Ordinal), JsonOptions);
            string tempPath = $"{storePath}.{Guid.NewGuid():N}.tmp";
            try
            {
                File.WriteAllText(tempPath, document);
                if (File.Exists(storePath))
                {
                    File.Replace(tempPath, storePath, destinationBackupFileName: null, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(tempPath, storePath, overwrite: false);
                }
            }
            catch (IOException)
            {
                TryDeleteTempFile(tempPath);
            }
            catch (UnauthorizedAccessException)
            {
                TryDeleteTempFile(tempPath);
            }
        }
    }

    private void EnsureLoaded()
    {
        if (approvedApps is not null)
        {
            return;
        }

        if (!File.Exists(storePath))
        {
            approvedApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return;
        }

        try
        {
            string json = File.ReadAllText(storePath);
            string[] values = JsonSerializer.Deserialize<string[]>(json, JsonOptions) ?? Array.Empty<string>();
            approvedApps = values
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Select(Normalize)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            approvedApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            approvedApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (UnauthorizedAccessException)
        {
            approvedApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string Normalize(string appId) => ComputerUseWinAppIdentity.NormalizeAppId(appId);

    private static void TryDeleteTempFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
