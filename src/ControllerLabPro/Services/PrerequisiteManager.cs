using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace ControllerLabPro.Services;

public static class PrerequisiteManager
{
    const string ViGEmUrl = "https://github.com/nefarius/ViGEmBus/releases/download/v1.22.0/ViGEmBus_1.22.0_x64_x86_arm64.exe";
    const string ViGEmSha256 = "89220A7865076B342892F98865F3499FB7C4CFD673159E89D352C360FD014C6A";

    public static bool IsViGEmInstalled()
    {
        var driver = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "drivers", "ViGEmBus.sys");
        if (File.Exists(driver)) return true;
        return RegistryContainsViGEm(RegistryView.Registry64) || RegistryContainsViGEm(RegistryView.Registry32);
    }

    static bool RegistryContainsViGEm(RegistryView view)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall is null) return false;
            foreach (var name in uninstall.GetSubKeyNames())
            {
                using var key = uninstall.OpenSubKey(name);
                if (key?.GetValue("DisplayName") is string display && display.Contains("ViGEm", StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        catch { }
        return false;
    }

    public static async Task InstallViGEmAsync(IProgress<string>? progress = null)
    {
        var downloadPath = Path.Combine(Path.GetTempPath(), "ControllerLabPro-ViGEmBus-1.22.0.exe");
        progress?.Report("Downloading the official ViGEmBus 1.22.0 installer…");
        using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
        using (var response = await client.GetAsync(ViGEmUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync();
            await using var target = new FileStream(downloadPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await source.CopyToAsync(target);
        }

        progress?.Report("Verifying the installer…");
        await using (var stream = File.OpenRead(downloadPath))
        {
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream));
            if (!actual.Equals(ViGEmSha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(downloadPath);
                throw new InvalidDataException("The ViGEmBus installer checksum did not match the official release. Installation was stopped.");
            }
        }

        progress?.Report("Waiting for Windows administrator approval…");
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = downloadPath,
                Arguments = "/exenoui /qn /norestart",
                UseShellExecute = true,
                Verb = "runas"
            }) ?? throw new InvalidOperationException("Windows did not start the ViGEmBus installer.");
            await process.WaitForExitAsync();
            if (process.ExitCode is not (0 or 3010)) throw new InvalidOperationException($"The ViGEmBus installer exited with code {process.ExitCode}.");
        }
        finally
        {
            try { File.Delete(downloadPath); } catch { }
        }
    }
}
