using CefSharp;
using CefSharp.OffScreen;
using NewTek;

namespace Tractus.HtmlToNdi.Chromium;

public class CefWrapper : IDisposable
{
    private volatile bool disposedValue;
    private ChromiumWebBrowser? browser;

    public int Width { get; private set; }
    public int Height { get; private set; }

    public string? Url { get; private set; }

    private readonly Thread renderWatchdog;
    private DateTime lastPaint = DateTime.MinValue;

    public CefWrapper(int width, int height, string initialUrl)
    {
        this.Width = width;
        this.Height = height;
        this.Url = initialUrl;

        this.browser = new ChromiumWebBrowser(initialUrl)
        {
            AudioHandler = new CustomAudioHandler(),
        };

        this.browser.Size = new System.Drawing.Size(this.Width, this.Height);

        this.renderWatchdog = new Thread(this.RenderWatchDogThread)
        {
            IsBackground = true,
            Name = "CEF render watchdog"
        };
    }

    private void RenderWatchDogThread()
    {
        while (!this.disposedValue)
        {
            // CEF only paints when something changes. Force a repaint at least once a
            // second so NDI receivers keep getting frames for static pages.
            if (DateTime.UtcNow.Subtract(this.lastPaint).TotalSeconds >= 1.0)
            {
                try
                {
                    this.browser?.GetBrowserHost()?.Invalidate(PaintElementType.View);
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
            }

            Thread.Sleep(1000);
        }
    }

    public async Task InitializeWrapperAsync()
    {
        if (this.browser is null)
        {
            return;
        }

        await this.browser.WaitForInitialLoadAsync();

        this.browser.GetBrowserHost().WindowlessFrameRate = 60;
        this.browser.ToggleAudioMute();

        this.browser.Paint += this.OnBrowserPaint;
        this.renderWatchdog.Start();
    }

    private void OnBrowserPaint(object? sender, OnPaintEventArgs e)
    {
        var senderPtr = Program.NdiSenderPtr;
        if (senderPtr == nint.Zero)
        {
            return;
        }

        this.lastPaint = DateTime.UtcNow;

        var videoFrame = new NDIlib.video_frame_v2_t()
        {
            FourCC = NDIlib.FourCC_type_e.FourCC_type_BGRA,
            frame_rate_N = 60,
            frame_rate_D = 1,
            frame_format_type = NDIlib.frame_format_type_e.frame_format_type_progressive,
            line_stride_in_bytes = e.Width * 4,
            picture_aspect_ratio = (float)e.Width / e.Height,
            p_data = e.BufferHandle,
            timecode = NDIlib.send_timecode_synthesize,
            xres = e.Width,
            yres = e.Height,
        };

        NDIlib.send_send_video_v2(senderPtr, ref videoFrame);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!this.disposedValue)
        {
            this.disposedValue = true;

            if (disposing)
            {
                if (this.renderWatchdog.IsAlive)
                {
                    this.renderWatchdog.Join(TimeSpan.FromSeconds(2));
                }

                if (this.browser is not null)
                {
                    this.browser.Paint -= this.OnBrowserPaint;
                    this.browser.Dispose();
                }

                this.browser = null;
            }
        }
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        this.Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    public void SetUrl(string url)
    {
        if (this.browser is null)
        {
            return;
        }

        this.Url = url;

        this.browser.Load(url);
    }

    public void ScrollBy(int increment)
    {
        this.browser?.SendMouseWheelEvent(0, 0, 0, increment, CefEventFlags.None);
    }

    public void Click(int x, int y)
    {
        var host = this.browser?.GetBrowser()?.GetHost();

        if (host is null)
        {
            return;
        }

        host.SendMouseClickEvent(x, y,
            MouseButtonType.Left, false, 1, CefEventFlags.None);
        Thread.Sleep(100);
        host.SendMouseClickEvent(x, y,
            MouseButtonType.Left, true, 1, CefEventFlags.None);
    }

    public void SendKeystrokes(Models.SendKeystrokeModel model)
    {
        var host = this.browser?.GetBrowser()?.GetHost();

        if (host is null || string.IsNullOrEmpty(model.ToSend))
        {
            return;
        }

        // Text input in CEF is delivered through KEYEVENT_CHAR, with the UTF-16 code
        // unit in WindowsKeyCode.
        foreach (var c in model.ToSend)
        {
            host.SendKeyEvent(new KeyEvent()
            {
                Type = KeyEventType.Char,
                WindowsKeyCode = c,
            });
        }
    }

    public void RefreshPage()
    {
        this.browser?.Reload();
    }
}
