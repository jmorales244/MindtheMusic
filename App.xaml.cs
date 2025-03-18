using System.Configuration;
using System.Data;
using System.Runtime.InteropServices;
using System.Windows;

namespace MindtheMusic;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    // Import AllocConsole from kernel32.dll to enable console output
    [DllImport("Kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool AllocConsole();
    
    public App()
    {
        // Uncomment the following line to enable console output for debugging
        //AllocConsole();
    }
}

