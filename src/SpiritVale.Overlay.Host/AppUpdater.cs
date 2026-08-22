using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SpiritVale.Overlay.Host;

internal static class NativeFileDialog
{
    [DllImport("comdlg32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetOpenFileName(ref OpenFileName ofn);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenFileName
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public string lpstrFilter;
        public string? lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public string lpstrFile;
        public int nMaxFile;
        public string? lpstrFileTitle;
        public int nMaxFileTitle;
        public string? lpstrInitialDir;
        public string? lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        public string? lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public string? lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    public static string? PickZipFile(string title = "Import plugin zip")
    {
        var buffer = new string('\0', 1024);
        var ofn = new OpenFileName
        {
            lStructSize = Marshal.SizeOf<OpenFileName>(),
            lpstrFilter = "Zip archives (*.zip)\0*.zip\0All files (*.*)\0*.*\0",
            lpstrFile = buffer,
            nMaxFile = buffer.Length,
            lpstrTitle = title,
            Flags = 0x00080000 | 0x00001000 | 0x00000008, // OFN_EXPLORER | OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST
            lpstrDefExt = "zip",
        };

        return GetOpenFileName(ref ofn) ? ofn.lpstrFile.Split('\0')[0] : null;
    }
}

internal static class AppUpdater
{
    public static async Task<string> ApplyUpdateAsync(
        CatalogClient catalog,
        AppUpdateInfo info,
        Action<string>? progress = null,
        CancellationToken ct = default)
    {
        OverlayPaths.Ensure();
        Directory.CreateDirectory(OverlayPaths.CacheDir);

        if (info.Installer is { } installer && !string.IsNullOrWhiteSpace(installer.DownloadUrl))
        {
            var dest = Path.Combine(OverlayPaths.CacheDir, installer.Filename);
            await catalog.DownloadAppArtifactAsync(installer, dest, progress, ct);
            progress?.Invoke("Launching installer…");
            Process.Start(new ProcessStartInfo
            {
                FileName = dest,
                UseShellExecute = true,
            });
            return dest;
        }

        if (info.Portable is { } portable && !string.IsNullOrWhiteSpace(portable.DownloadUrl))
        {
            var dest = Path.Combine(OverlayPaths.CacheDir, portable.Filename);
            await catalog.DownloadAppArtifactAsync(portable, dest, progress, ct);
            progress?.Invoke("Portable zip downloaded — extract over your install folder.");
            ProtocolHandler.OpenInExplorer(dest);
            return dest;
        }

        throw new InvalidOperationException("No installer or portable build is published yet.");
    }
}
