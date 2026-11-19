using NewTek;
using Serilog;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Tractus.HtmlToNdi;

/// <summary>
/// Locates and initializes the NDI runtime.
/// </summary>
/// <remarks>
/// NDILibDotNetCoreBase only looks for <c>Processing.NDI.Lib.x64.dll</c> next to the
/// executable or on the default DLL search path. The NDI Runtime / NDI Tools installers
/// put it in the folder named by <c>NDI_RUNTIME_DIR_V6</c> (or older _V5/_V4/... variables)
/// and do not add it to PATH, so we preload it from there. Once a module is loaded,
/// Windows resolves later loads of the same file name to it.
/// </remarks>
public static class NdiRuntime
{
    private const string WindowsLibraryName = "Processing.NDI.Lib.x64.dll";

    public static bool Initialize()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            PreloadWindowsRuntime();
        }

        // NDIlib registers its DllImport resolver in its static constructor, and the type
        // is beforefieldinit, so make sure it has run before the first P/Invoke.
        RuntimeHelpers.RunClassConstructor(typeof(NDIlib).TypeHandle);

        try
        {
            return NDIlib.initialize();
        }
        catch (DllNotFoundException ex)
        {
            Log.Error(ex, "The NDI runtime ({Library}) could not be found. Install the NDI 6 Runtime or NDI Tools from https://ndi.video/tools/, or place {Library} next to the executable.", WindowsLibraryName, WindowsLibraryName);
            return false;
        }
    }

    private static void PreloadWindowsRuntime()
    {
        var localCopy = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, WindowsLibraryName);
        if (File.Exists(localCopy))
        {
            // A bundled copy takes precedence; the wrapper loads it itself.
            return;
        }

        foreach (var variable in new[] { "NDI_RUNTIME_DIR_V6", "NDI_RUNTIME_DIR_V5", "NDI_RUNTIME_DIR_V4", "NDI_RUNTIME_DIR_V3" })
        {
            var directory = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            var candidate = Path.Combine(directory, WindowsLibraryName);
            if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out _))
            {
                Log.Debug("Loaded NDI runtime from {Path}", candidate);
                return;
            }
        }
    }
}
