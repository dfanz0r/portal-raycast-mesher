using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform;
using MeshTool.UI.Models;

namespace MeshTool.UI.Controllers;

public sealed class ScanScriptExportController
{
    public async Task ExportToClipboardAsync(TopLevel topLevel, ScanVolumeSettings settings, float fineTargetStep, Action<string> log)
    {
        string template = LoadEmbeddedScanTemplate();
        string generated = GenerateScanScript(template, settings, fineTargetStep);

        if (topLevel.Clipboard == null)
        {
            throw new InvalidOperationException("Clipboard not available.");
        }

        await topLevel.Clipboard.SetTextAsync(generated);
        log("[SCAN] Raycast script copied to clipboard.");
    }

    private static string GenerateScanScript(string template, ScanVolumeSettings s, float fineTargetStep)
    {
        string output = template;
        float halfX = s.SizeX * 0.5f;
        float halfZ = s.SizeZ * 0.5f;
        float maxHalf = MathF.Max(halfX, halfZ);

        var tokens = new Dictionary<string, string>
        {
            ["MAP_CENTER_X"] = s.CenterX.ToString("0.###", CultureInfo.InvariantCulture),
            ["MAP_CENTER_Z"] = s.CenterZ.ToString("0.###", CultureInfo.InvariantCulture),
            ["MAP_HALF_SIZE_X"] = halfX.ToString("0.###", CultureInfo.InvariantCulture),
            ["MAP_HALF_SIZE_Z"] = halfZ.ToString("0.###", CultureInfo.InvariantCulture),
            ["SCAN_YAW_DEG"] = s.YawDegrees.ToString("0.###", CultureInfo.InvariantCulture),
            ["SCAN_TILT_DEG"] = s.RayTiltDegrees.ToString("0.###", CultureInfo.InvariantCulture),
            ["Y_TOP"] = s.YTop.ToString("0.###", CultureInfo.InvariantCulture),
            ["Y_BOTTOM"] = s.YBottom.ToString("0.###", CultureInfo.InvariantCulture),
            ["INITIAL_PROBE_CELL_SIZE"] = s.ProbeCellSize.ToString("0.###", CultureInfo.InvariantCulture),
            ["INITIAL_PROBE_RADIUS"] = maxHalf.ToString("0.###", CultureInfo.InvariantCulture),
            ["TARGET_STEP"] = MathF.Round(fineTargetStep).ToString("0", CultureInfo.InvariantCulture),
            ["BOT_COUNT"] = "5"
        };

        foreach (var kv in tokens)
        {
            output = output.Replace($"{{{{{kv.Key}}}}}", kv.Value, StringComparison.Ordinal);
        }

        if (output.Contains("{{", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("scan.ts.template contains unresolved tokens.");
        }

        return output.TrimEnd() + Environment.NewLine;
    }

    private static string LoadEmbeddedScanTemplate()
    {
        var uri = new Uri("avares://MeshTool.UI/scan.ts.template");
        if (!AssetLoader.Exists(uri))
        {
            throw new InvalidOperationException("Embedded resource 'scan.ts.template' not found.");
        }

        using var stream = AssetLoader.Open(uri);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
