using System;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace ReversalCrop.App;

internal static partial class SystemFontProvider
{
    public static string GetPreferredFontFamily()
    {
        if (!OperatingSystem.IsLinux())
            return "sans-serif";

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "gsettings",
                ArgumentList = { "get", "org.gnome.desktop.interface", "font-name" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null || !process.WaitForExit(500))
            {
                process?.Kill(entireProcessTree: true);
                return "sans-serif";
            }

            string description = process.StandardOutput.ReadToEnd().Trim().Trim('\'', '"');
            string family = FontSizeSuffixRegex().Replace(description, "").Trim();
            return string.IsNullOrWhiteSpace(family) ? "sans-serif" : family;
        }
        catch
        {
            return "sans-serif";
        }
    }

    [GeneratedRegex(@"\s+\d+(?:\.\d+)?$")]
    private static partial Regex FontSizeSuffixRegex();
}
