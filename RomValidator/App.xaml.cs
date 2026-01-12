using System.IO;
using System.Windows;
using SevenZip;

namespace RomValidator;

public partial class App
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Initialize SevenZipSharp with the path to 7z.dll
        var dllPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "7z.dll");
        if (File.Exists(dllPath))
        {
            SevenZipBase.SetLibraryPath(dllPath);
        }
        
        base.OnStartup(e);
    }
}