using CefSharp;
using CefSharp.OffScreen;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using NewTek;
using NewTek.NDI;
using Serilog;
using System.Runtime.InteropServices;
using Tractus.HtmlToNdi.Chromium;
using Tractus.HtmlToNdi.Models;

namespace Tractus.HtmlToNdi;

public class Program
{
    public static nint NdiSenderPtr;
    public static CefWrapper? browserWrapper;

    public static void Main(string[] args)
    {
        var exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
        var launchCachePath = Path.Combine(exeDirectory, "cache", Guid.NewGuid().ToString());

        Directory.SetCurrentDirectory(exeDirectory);
        AppManagement.Initialize(args);

        string ndiName;
        if (TryGetArgValue(args, out var ndiNameArg, "--ndiname"))
        {
            if (string.IsNullOrWhiteSpace(ndiNameArg))
            {
                Log.Error("Invalid NDI source name. Exiting.");
                return;
            }

            ndiName = ndiNameArg;
        }
        else
        {
            ndiName = "";
            while (string.IsNullOrWhiteSpace(ndiName))
            {
                Console.Write("NDI source name >");
                ndiName = Console.ReadLine()?.Trim() ?? "";
            }
        }

        int port;
        if (TryGetArgValue(args, out var portArg, "--port"))
        {
            if (!int.TryParse(portArg, out port))
            {
                Log.Error("Could not parse the --port parameter. Exiting.");
                return;
            }
        }
        else
        {
            var portNumber = "";
            while (string.IsNullOrWhiteSpace(portNumber) || !int.TryParse(portNumber, out port))
            {
                Console.Write("HTTP API port # >");
                portNumber = Console.ReadLine()?.Trim();
            }
        }

        var startUrl = "https://testpattern.tractusevents.com/";
        if (TryGetArgValue(args, out var urlArg, "--url"))
        {
            if (string.IsNullOrWhiteSpace(urlArg))
            {
                Log.Error("Could not parse the --url parameter. Exiting.");
                return;
            }

            startUrl = urlArg;
        }

        var width = 1920;
        var height = 1080;

        if (TryGetArgValue(args, out var widthArg, "--width", "--w")
            && !int.TryParse(widthArg, out width))
        {
            Log.Error("Could not parse the --width parameter. Exiting.");
            return;
        }

        if (TryGetArgValue(args, out var heightArg, "--height", "--h")
            && !int.TryParse(heightArg, out height))
        {
            Log.Error("Could not parse the --height parameter. Exiting.");
            return;
        }

        if (!NdiRuntime.Initialize())
        {
            Log.Error("Could not initialize NDI. Make sure the NDI runtime is installed and this CPU is supported. Exiting.");
            return;
        }

        var cefInitialized = false;
        AsyncContext.Run(async delegate
        {
            var settings = new CefSettings();
            if (!Directory.Exists(launchCachePath))
            {
                Directory.CreateDirectory(launchCachePath);
            }

            // Each instance gets its own root cache path so multiple instances can run
            // side by side (CEF enforces a process singleton per cache path).
            settings.RootCachePath = launchCachePath;
            // Keep CEF's log out of the per-launch cache folder: the log stays open until the
            // process exits, which would otherwise prevent that folder from being deleted.
            settings.LogFile = Path.Combine(exeDirectory, "cef_debug.log");
            settings.CefCommandLineArgs.Add("autoplay-policy", "no-user-gesture-required");
            settings.EnableAudio();

            if (!Cef.Initialize(settings))
            {
                Log.Error("Cef.Initialize failed. Exiting.");
                return;
            }

            cefInitialized = true;

            browserWrapper = new CefWrapper(
                width,
                height,
                startUrl);

            await browserWrapper.InitializeWrapperAsync();
        });

        if (!cefInitialized || browserWrapper is null)
        {
            NDIlib.destroy();
            return;
        }

        var browser = browserWrapper;

        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddSerilog();

        builder.WebHost.UseUrls($"http://*:{port}");

        builder.Services.AddAuthorization();

        // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        var app = builder.Build();
        app.UseSwagger();
        app.UseSwaggerUI();

        var settings_T = new NDIlib.send_create_t
        {
            p_ndi_name = UTF.StringToUtf8(ndiName)
        };

        Program.NdiSenderPtr = NDIlib.send_create(ref settings_T);
        Marshal.FreeHGlobal(settings_T.p_ndi_name);

        if (Program.NdiSenderPtr == nint.Zero)
        {
            Log.Error("Could not create the NDI sender. Exiting.");
            browser.Dispose();
            Cef.Shutdown();
            NDIlib.destroy();
            return;
        }

        var capabilitiesXml = $$"""<ndi_capabilities ntk_kvm="true" />""";
        capabilitiesXml += "\0";
        var capabilitiesPtr = UTF.StringToUtf8(capabilitiesXml);

        var metaframe = new NDIlib.metadata_frame_t()
        {
            p_data = capabilitiesPtr
        };

        NDIlib.send_add_connection_metadata(NdiSenderPtr, ref metaframe);
        Marshal.FreeHGlobal(capabilitiesPtr);

        var running = true;
        var thread = new Thread(() =>
        {
            var metadata = new NDIlib.metadata_frame_t();
            var x = 0.0f;
            var y = 0.0f;
            while (Volatile.Read(ref running))
            {
                var result = NDIlib.send_capture(NdiSenderPtr, ref metadata, 1000);

                if (result != NDIlib.frame_type_e.frame_type_metadata)
                {
                    continue;
                }

                var metadataConverted = UTF.Utf8ToString(metadata.p_data) ?? "";
                NDIlib.send_free_metadata(NdiSenderPtr, ref metadata);

                Log.Debug("Got metadata: {Metadata}", metadataConverted);

                if (!metadataConverted.StartsWith("<ndi_kvm u=\""))
                {
                    continue;
                }

                metadataConverted = metadataConverted.Replace("<ndi_kvm u=\"", "");
                metadataConverted = metadataConverted.Replace("\"/>", "");

                try
                {
                    var binary = Convert.FromBase64String(metadataConverted);

                    var opcode = binary[0];

                    if (opcode == 0x03)
                    {
                        x = BitConverter.ToSingle(binary, 1);
                        y = BitConverter.ToSingle(binary, 5);
                    }
                    else if (opcode == 0x04)
                    {
                        // Mouse Left Down
                        var screenX = (int)(x * width);
                        var screenY = (int)(y * height);

                        browser.Click(screenX, screenY);
                    }
                    else if (opcode == 0x07)
                    {
                        // Mouse Left Up
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Could not handle KVM metadata.");
                }
            }
        })
        {
            IsBackground = true,
            Name = "NDI metadata"
        };
        thread.Start();

        app.MapPost("/seturl", (GoToUrlModel url) =>
        {
            browser.SetUrl(url.Url);
            return true;
        });

        app.MapGet("/scroll/{increment}", (int increment) =>
        {
            browser.ScrollBy(increment);
        });

        app.MapGet("/click/{x}/{y}", (int x, int y) =>
        {
            browser.Click(x, y);
        });

        app.MapPost("/keystroke", (SendKeystrokeModel model) =>
        {
            browser.SendKeystrokes(model);
        });

        app.MapGet("/type/{toType}", (string toType) =>
        {
            browser.SendKeystrokes(new SendKeystrokeModel
            {
                ToSend = toType
            });
        });

        app.MapGet("/refresh", () =>
        {
            browser.RefreshPage();
        });

        app.Run();

        Volatile.Write(ref running, false);
        thread.Join(TimeSpan.FromSeconds(2));
        browser.Dispose();

        var senderPtr = Program.NdiSenderPtr;
        Program.NdiSenderPtr = nint.Zero;
        NDIlib.send_destroy(senderPtr);

        Cef.Shutdown();
        NDIlib.destroy();

        DeleteCacheDirectory(launchCachePath);
    }

    private static void DeleteCacheDirectory(string path)
    {
        // Chromium's subprocesses can hold file handles for a moment after Cef.Shutdown(),
        // so retry briefly before giving up.
        for (var attempt = 0; attempt < 10 && Directory.Exists(path); attempt++)
        {
            try
            {
                Directory.Delete(path, true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(250);
            }
        }

        if (Directory.Exists(path))
        {
            Log.Warning("Could not delete the browser cache directory {Path}", path);
        }
    }

    /// <summary>
    /// Looks for an argument in the form <c>--name=value</c> (or <c>--name value</c>)
    /// matching any of the given names. Only the first '=' is treated as the separator,
    /// so values such as URLs with query strings are preserved.
    /// </summary>
    private static bool TryGetArgValue(string[] args, out string? value, params string[] names)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            foreach (var name in names)
            {
                if (arg.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    value = i + 1 < args.Length ? args[i + 1].Trim().Trim('"') : null;
                    return true;
                }

                if (arg.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
                {
                    value = arg[(name.Length + 1)..].Trim().Trim('"');
                    return true;
                }
            }
        }

        value = null;
        return false;
    }
}
