# Tractus HTML to NDI Utility

A simple wrapper around [CEFSharp](https://github.com/cefsharp/CefSharp) and [NDI Wrapper for .NET](https://github.com/eliaspuurunen/NdiLibDotNetCoreBase). Sends HTML pages to NDI, including audio if playback on the page is supported.

[Grab the latest ZIP file](https://github.com/tractusevents/Tractus.HtmlToNdi/releases) from the releases page.

## Requirements

- Windows 10 or 11, x64
- [.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (the ASP.NET Core Runtime, which includes the .NET Runtime)
- [Microsoft Visual C++ 2022 Redistributable (x64)](https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist), required by Chromium
- The NDI runtime: install the [NDI 6 Runtime or NDI Tools](https://ndi.video/tools/). The app finds `Processing.NDI.Lib.x64.dll` through the `NDI_RUNTIME_DIR_V6` environment variable those installers set, or you can place the DLL next to the executable.

## Usage

Launch as-is for a 1920x1080 browser instance. The app will ask you for a source name and HTTP port if they are not provided on the command line.

You can run several instances at the same time, as long as each uses its own NDI name and port.

If the web page you are loading has a transparent background, NDI will honor that transparency.

## Command Line Parameters

Parameter|Description
----|---
`--ndiname="NDI Source Name"`|The source name this browser instance will send. Prompted for if omitted.
`--width=1920` (or `--w=1920`)|The width of the browser source. Defaults to `1920`.
`--height=1080` (or `--h=1080`)|The height of the browser source. Defaults to `1080`.
`--port=9999`|The port the HTTP server will listen on. Prompted for if omitted.
`--url="https://www.tractus.ca"`|The startup webpage. Defaults to `https://testpattern.tractusevents.com/`. Query strings (`?a=b`) are supported.
`-debug`|Enables debug-level logging.
`-quiet`|Disables console logging. The log file is still written.

Logs are written to `Tractus.HtmlToNdi_log<date>.txt` in your Documents folder. Chromium's own log goes to `cef_debug.log` next to the executable.

#### Example Launch

`.\Tractus.HtmlToNdi.exe --ndiname="HTML 5 Test" --w=1080 --h=1080 --url="https://testpattern.tractusevents.com"`

## API Routes

Interactive API documentation (Swagger UI) is available at `http://localhost:<port>/swagger`.

Route|Method|Description|Example
----|----|----|---
`/seturl`|`POST`|Sets the URL for this instance.|`{"url": "https://www.google.ca"}`
`/refresh`|`GET`|Reloads the current page.|`/refresh`
`/scroll/{increment}`|`GET`|Scrolls the page vertically by `increment` pixels (negative scrolls down).|`/scroll/-120`
`/click/{x}/{y}`|`GET`|Left-clicks at the given pixel coordinates.|`/click/960/540`
`/type/{text}`|`GET`|Types the given text into the focused element.|`/type/Hello%20world`
`/keystroke`|`POST`|Types the given text into the focused element.|`{"toSend": "Hello world"}`

The NDI source also advertises KVM support, so NDI receivers that support KVM (for example NDI Studio Monitor) can click on the page.

## Known Limitations

- Frames are sent to NDI in BGRA format. Some machines may experience a slight performance penalty.
- H.264 and any other non-free codecs are not available for video playback since this uses Chromium. Sites like YouTube likely won't work.
- Audio data received from the browser is passed to NDI directly.
- NDI frame rate is set to 60 frames per second. The internal max render frame rate is set to be capped at 60 frames per second. Chromium only renders when the page changes, so static pages are re-sent once per second.

## Building

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```
dotnet build Tractus.HtmlToNdi.csproj -c Release
dotnet publish Tractus.HtmlToNdi.csproj -c Release -o publish
```

## More Tools

We have more tools available at [our website, tractusevents.com](https://www.tractusevents.com/tools).
