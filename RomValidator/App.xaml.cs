using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using SevenZip;

namespace RomValidator;

public partial class App
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Initialize SevenZipSharp with the correct 7z.dll based on architecture
        var architecture = RuntimeInformation.ProcessArchitecture;
        var dllName = architecture == Architecture.Arm64 ? "7z_arm64.dll" : "7z_x64.dll";
        var dllPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, dllName);

        if (File.Exists(dllPath))
        {
            SevenZipBase.SetLibraryPath(dllPath);
        }
        else
        {
            // Fallback error logging
            System.Diagnostics.Debug.WriteLine($"SevenZipSharp library not found at: {dllPath}");
        }

        base.OnStartup(e);
    }
}