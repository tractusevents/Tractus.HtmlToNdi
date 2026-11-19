
using CefSharp;
using CefSharp.Enums;
using CefSharp.Structs;
using NewTek;
using System.Runtime.InteropServices;

namespace Tractus.HtmlToNdi.Chromium;


public class CustomAudioHandler : IAudioHandler
{
    public static readonly Dictionary<ChannelLayout, int> ChannelLayoutToChannelCount = new Dictionary<ChannelLayout, int>
    {
        { ChannelLayout.LayoutNone, 0 },
        { ChannelLayout.LayoutUnsupported, 0 },
        { ChannelLayout.LayoutMono, 1 },
        { ChannelLayout.LayoutStereo, 2 },
        { ChannelLayout.Layout2_1, 3 },
        { ChannelLayout.LayoutSurround, 4 },
        { ChannelLayout.Layout4_0, 4 },
        { ChannelLayout.Layout2_2, 4 },
        { ChannelLayout.LayoutQuad, 4 },
        { ChannelLayout.Layout5_0, 5 },
        { ChannelLayout.Layout5_1, 6 },
        { ChannelLayout.Layout5_0Back, 5 },
        { ChannelLayout.Layout5_1Back, 6 },
        { ChannelLayout.Layout7_0, 7 },
        { ChannelLayout.Layout7_1, 8 },
        { ChannelLayout.Layout7_1Wide, 8 },
        { ChannelLayout.LayoutStereoDownMix, 2 },
        { ChannelLayout.Layout2Point1, 3 },
        { ChannelLayout.Layout3_1, 4 },
        { ChannelLayout.Layout4_1, 5 },
        { ChannelLayout.Layout6_0, 6 },
        { ChannelLayout.Layout6_0Front, 6 },
        { ChannelLayout.LayoutHexagonal, 6 },
        { ChannelLayout.Layout6_1, 7 },
        { ChannelLayout.Layout6_1Back, 7 },
        { ChannelLayout.Layout6_1Front, 7 },
        { ChannelLayout.Layout7_0Front, 7 },
        { ChannelLayout.Layout7_1WideBack, 8 },
        { ChannelLayout.LayoutOctagonal, 8 },
        { ChannelLayout.LayoutStereoKeyboardAndMic, 2 },
        { ChannelLayout.Layout4_1QuadSize, 5 },
        { ChannelLayout.Layout5_1_4DownMix, 6 },
    };

    private readonly object sync = new();
    private AudioParameters audioParameters;
    private nint audioBufferPtr;
    private int audioBufferLengthInBytes;
    private int channelCount;

    public void Dispose()
    {
        lock (this.sync)
        {
            this.FreeBuffer();
        }
    }

    private void FreeBuffer()
    {
        if (this.audioBufferPtr != nint.Zero)
        {
            Marshal.FreeHGlobal(this.audioBufferPtr);
            this.audioBufferPtr = nint.Zero;
        }

        this.audioBufferLengthInBytes = 0;
    }

    private void EnsureBufferCapacity(int lengthInBytes)
    {
        if (this.audioBufferPtr != nint.Zero && this.audioBufferLengthInBytes >= lengthInBytes)
        {
            return;
        }

        this.FreeBuffer();
        this.audioBufferPtr = Marshal.AllocHGlobal(lengthInBytes);
        this.audioBufferLengthInBytes = lengthInBytes;
    }

    public bool GetAudioParameters(IWebBrowser chromiumWebBrowser, IBrowser browser, ref AudioParameters parameters)
    {
        lock (this.sync)
        {
            this.audioParameters = parameters;

            // Best guess until OnAudioStreamStarted tells us the real channel count.
            this.channelCount = ChannelLayoutToChannelCount.TryGetValue(parameters.ChannelLayout, out var count) && count > 0
                ? count
                : 2;

            // Pre-allocate 1s worth of planar float data; grown on demand if a packet is bigger.
            this.EnsureBufferCapacity(parameters.SampleRate * this.channelCount * sizeof(float));
        }

        return true;
    }

    public void OnAudioStreamError(IWebBrowser chromiumWebBrowser, IBrowser browser, string errorMessage)
    {
        Serilog.Log.Warning("Audio stream error: {Error}", errorMessage);
    }

    public unsafe void OnAudioStreamPacket(IWebBrowser chromiumWebBrowser, IBrowser browser, nint data, int noOfFrames, long pts)
    {
        var senderPtr = Program.NdiSenderPtr;
        if (senderPtr == nint.Zero || data == nint.Zero || noOfFrames <= 0)
        {
            return;
        }

        lock (this.sync)
        {
            if (this.channelCount <= 0)
            {
                return;
            }

            var channelLengthInBytes = sizeof(float) * noOfFrames;
            this.EnsureBufferCapacity(channelLengthInBytes * this.channelCount);

            // CEF hands us an array of per-channel float pointers; NDI wants the planes
            // laid out back to back (FLTP), so copy each plane into one contiguous buffer.
            var dataPtr = (float**)data;

            for (var c = 0; c < this.channelCount; c++)
            {
                Buffer.MemoryCopy(
                    dataPtr[c],
                    nint.Add(this.audioBufferPtr, channelLengthInBytes * c).ToPointer(),
                    channelLengthInBytes,
                    channelLengthInBytes);
            }

            var audioFrame = new NDIlib.audio_frame_v2_t
            {
                channel_stride_in_bytes = channelLengthInBytes,
                p_data = this.audioBufferPtr,
                no_channels = this.channelCount,
                no_samples = noOfFrames,
                sample_rate = this.audioParameters.SampleRate,
                // CEF's pts is in milliseconds while NDI timecodes are in 100ns units;
                // let NDI synthesize it, matching what the video path does.
                timecode = NDIlib.send_timecode_synthesize,
            };

            NDIlib.send_send_audio_v2(senderPtr, ref audioFrame);
        }
    }

    public void OnAudioStreamStarted(IWebBrowser chromiumWebBrowser, IBrowser browser, AudioParameters parameters, int channels)
    {
        lock (this.sync)
        {
            this.audioParameters = parameters;

            if (channels > 0)
            {
                this.channelCount = channels;
            }
        }
    }

    public void OnAudioStreamStopped(IWebBrowser chromiumWebBrowser, IBrowser browser)
    {
    }
}
