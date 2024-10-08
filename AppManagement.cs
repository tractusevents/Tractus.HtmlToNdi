using Serilog;
using Serilog.Core;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Tractus.HtmlToNdi;
public static class AppManagement
{
    public static bool IsFirstRun { get; set; }

    public static LoggingLevelSwitch LoggingLevel { get; set; } = new LoggingLevelSwitch();

    public static string DataDirectory
    {
        get
        {
            return AppDomain.CurrentDomain.BaseDirectory;
        }
    }

    public static void DeleteFileFromDataDirectory(string fileName)
    {
        var path = Path.Combine(DataDirectory, fileName);

        File.Delete(path);
    }

    public static bool FileExistsInDataDirectory(string fileName)
    {
        return File.Exists(Path.Combine(DataDirectory, fileName));
    }

    public static string[] ReadFileFromDataDirectory(string fileName)
    {
        var path = Path.Combine(DataDirectory, fileName);

        return File.ReadAllLines(path);
    }

    public static string ReadTextFromDataDirectory(string fileName)
    {
        var path = Path.Combine(DataDirectory, fileName);

        return File.ReadAllText(path);
    }

    public static void WriteFileToDataDirectory(string fileName, string[] lines)
    {
        var path = Path.Combine(DataDirectory, fileName);

        File.WriteAllLines(path, lines);
    }

    public static void WriteFileToDataDirectory(string fileName, string content)
    {
        var path = Path.Combine(DataDirectory, fileName);

        File.WriteAllText(path, content);
    }

    public static string AppName => Assembly.GetEntryAssembly()?.GetName()?.Name ?? "App Name Not Set";

    public static string Version => Assembly.GetExecutingAssembly()?.GetName()?.Version?.ToString() ?? "0.0.0.0";

    public static string InstanceName
    {
        get
        {
            var machineName = Environment.MachineName;

            var osPlatform = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "windows"
                : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? "macos"
                : RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                ? "linux"
                : "other";

            var bitness = RuntimeInformation.ProcessArchitecture == Architecture.X64
                ? "x86_64"
                : RuntimeInformation.ProcessArchitecture == Architecture.X86
                ? "x86_32"
                : RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? "arm64"
                : RuntimeInformation.ProcessArchitecture == Architecture.Arm
                ? "arm"
                : RuntimeInformation.ProcessArchitecture.ToString();

            return $"{osPlatform}_{bitness}_{machineName}";
        }
    }


    public static void Initialize(string[] args)
    {
        if (!Directory.Exists(AppManagement.DataDirectory))
        {
            IsFirstRun = true;
            Directory.CreateDirectory(DataDirectory);
        }

        var currentDomain = AppDomain.CurrentDomain;
        currentDomain.UnhandledException += OnAppDomainUnhandledException;

        var loggerConfig = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(LoggingLevel);

        var isDebugMode = args.Any(x => x.Equals("-debug"));

        if (isDebugMode)
        {
            LoggingLevel.MinimumLevel = Serilog.Events.LogEventLevel.Debug;
        }

        var quietMode = args.Any(x => x.Equals("-quiet"));

        if (!quietMode)
        {
            loggerConfig = loggerConfig.WriteTo.Console();
        }

        var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        loggerConfig = loggerConfig.WriteTo.File(System.IO.Path.Combine(
                documentsPath,
                $"{AppName}_log.txt"), rollingInterval: RollingInterval.Day);

        Log.Logger = loggerConfig.CreateLogger();

        Log.Information($"{AppName} starting up.");
    }

    private static void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var exception = (Exception)e.ExceptionObject;

        Log.Error("Unhandled exception in appdomain: {@ex}", exception);
        if (e.IsTerminating)
        {
            Log.Error("Runtime is terminating. Fatal exception.");
        }
        else
        {
            Log.Error("Runtime is not terminating.");
        }
    }
}
