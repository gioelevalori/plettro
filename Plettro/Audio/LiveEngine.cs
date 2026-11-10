using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.Asio;
using Plettro.Audio.Fx;

namespace Plettro.Audio;

/// <summary>
/// Live guitar processing: input → GuitarRig → output.
/// ASIO gives the lowest latency (Focusrite USB ASIO); WASAPI is a fallback.
/// </summary>
public sealed class LiveEngine : IDisposable
{
    AsioOut? asio;
    WasapiCapture? capture;
    WasapiOut? wasOut;
    BufferedWaveProvider? buffered;

    float[] mono = new float[8192];
    float[] interleaved = new float[16384];
    int inputChannel;
    int wasChannels, wasBytesPerSample, wasBlockAlign;
    bool wasFloat;

    public GuitarRig? Rig { get; private set; }
    public bool IsRunning => asio != null || capture != null;
    public int SampleRate { get; private set; }
    public double LatencyMs { get; private set; }
    public string Description { get; private set; } = "";
    public event Action<string>? Stopped;

    public static string[] AsioDrivers()
    {
        try { return AsioOut.GetDriverNames(); } catch { return Array.Empty<string>(); }
    }

    // ---------------------------------------------------------------- ASIO

    public void StartAsio(string driver, int inputChannel, Action<GuitarRig> configure, int preferredRate = 0)
    {
        Stop();
        var a = new AsioOut(driver);
        try
        {
            int rate = 48000;
            // use the rate the interface already runs at (changing it would disturb other apps and Plettro's own WASAPI streams)
            foreach (var r in new[] { preferredRate, 48000, 44100, 96000 }.Where(r => r > 0))
                if (a.IsSampleRateSupported(r)) { rate = r; break; }

            int inputs = a.DriverInputChannelCount;
            this.inputChannel = Math.Clamp(inputChannel, 0, Math.Max(0, inputs - 1));
            a.InputChannelOffset = this.inputChannel;
            a.ChannelOffset = 0;

            SampleRate = rate;
            Rig = new GuitarRig(rate);
            configure(Rig);

            a.AudioAvailable += OnAsioAudio;
            a.InitRecordAndPlayback(new SilenceProvider(WaveFormat.CreateIeeeFloatWaveFormat(rate, 2)), 1, rate);
            a.Play();
            asio = a;
            LatencyMs = a.PlaybackLatency * 2 * 1000.0 / rate; // in + out buffers
            Description = $"ASIO · {driver} · {rate / 1000.0:0.#} kHz · ingresso {this.inputChannel + 1}";
        }
        catch
        {
            try { a.Dispose(); } catch { }
            Rig = null;
            throw;
        }
    }

    public void ShowAsioPanel() { try { asio?.ShowControlPanel(); } catch { } }

    void OnAsioAudio(object? sender, AsioAudioAvailableEventArgs e)
    {
        var rig = Rig;
        if (rig == null) return;
        int n = e.SamplesPerBuffer;
        if (mono.Length < n) mono = new float[n];
        if (interleaved.Length < n * e.InputBuffers.Length) interleaved = new float[n * e.InputBuffers.Length];

        e.GetAsInterleavedSamples(interleaved);
        Array.Copy(interleaved, mono, n); // one recorded channel
        rig.Process(mono, n);

        for (int ch = 0; ch < e.OutputBuffers.Length; ch++)
            WriteAsio(e.OutputBuffers[ch], mono, n, e.AsioSampleType);
        e.WrittenToOutputBuffers = true;
    }

    static unsafe void WriteAsio(IntPtr dest, float[] src, int n, AsioSampleType type)
    {
        switch (type)
        {
            case AsioSampleType.Int32LSB:
            {
                var p = (int*)dest;
                for (int i = 0; i < n; i++) p[i] = (int)(Math.Clamp(src[i], -1f, 1f) * 2147483000f);
                break;
            }
            case AsioSampleType.Float32LSB:
            {
                var p = (float*)dest;
                for (int i = 0; i < n; i++) p[i] = src[i];
                break;
            }
            case AsioSampleType.Int16LSB:
            {
                var p = (short*)dest;
                for (int i = 0; i < n; i++) p[i] = (short)(Math.Clamp(src[i], -1f, 1f) * 32767f);
                break;
            }
            case AsioSampleType.Int24LSB:
            {
                var p = (byte*)dest;
                for (int i = 0; i < n; i++)
                {
                    int v = (int)(Math.Clamp(src[i], -1f, 1f) * 8388607f);
                    p[3 * i] = (byte)v; p[3 * i + 1] = (byte)(v >> 8); p[3 * i + 2] = (byte)(v >> 16);
                }
                break;
            }
            default:
            {
                var p = (int*)dest; // best guess
                for (int i = 0; i < n; i++) p[i] = (int)(Math.Clamp(src[i], -1f, 1f) * 2147483000f);
                break;
            }
        }
    }

    // ---------------------------------------------------------------- WASAPI

    public void StartWasapi(MMDevice input, MMDevice output, int inputChannel, Action<GuitarRig> configure)
    {
        Stop();
        var c = new WasapiCapture(input, true, 10);
        var wf = c.WaveFormat;
        wasChannels = wf.Channels;
        wasBlockAlign = wf.BlockAlign;
        wasBytesPerSample = wf.BitsPerSample / 8;
        wasFloat = wf.Encoding == WaveFormatEncoding.IeeeFloat || (wf is WaveFormatExtensible ext &&
                   ext.SubFormat == new Guid("00000003-0000-0010-8000-00aa00389b71"));
        this.inputChannel = Math.Clamp(inputChannel, 0, wasChannels - 1);

        SampleRate = wf.SampleRate;
        Rig = new GuitarRig(SampleRate);
        configure(Rig);

        var outFormat = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 2);
        buffered = new BufferedWaveProvider(outFormat)
        {
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromMilliseconds(300),
            ReadFully = true,
        };
        var o = new WasapiOut(output, AudioClientShareMode.Shared, true, 10);
        o.Init(buffered);

        c.DataAvailable += OnWasapiData;
        c.RecordingStopped += (_, e) => { if (e.Exception != null) Stopped?.Invoke(e.Exception.Message); };
        c.StartRecording();
        o.Play();
        capture = c;
        wasOut = o;
        LatencyMs = 10 + 10 + 15; // capture + render buffers + mixer, approximate
        Description = $"WASAPI · {input.FriendlyName} → {output.FriendlyName} · {SampleRate / 1000.0:0.#} kHz";
    }

    float ReadSample(byte[] b, int o)
    {
        if (wasFloat && wasBytesPerSample == 4) return BitConverter.ToSingle(b, o);
        return wasBytesPerSample switch
        {
            2 => BitConverter.ToInt16(b, o) / 32768f,
            3 => (b[o] | (b[o + 1] << 8) | ((sbyte)b[o + 2] << 16)) / 8388608f,
            4 => BitConverter.ToInt32(b, o) / 2147483648f,
            _ => 0f,
        };
    }

    void OnWasapiData(object? sender, WaveInEventArgs e)
    {
        var rig = Rig;
        var buf = buffered;
        if (rig == null || buf == null) return;
        int frames = e.BytesRecorded / wasBlockAlign;
        if (mono.Length < frames) mono = new float[frames];
        for (int i = 0; i < frames; i++)
            mono[i] = ReadSample(e.Buffer, i * wasBlockAlign + inputChannel * wasBytesPerSample);

        rig.Process(mono, frames);

        // keep latency low: if the output buffer grows, drop the excess
        if (buf.BufferedDuration.TotalMilliseconds > 40) buf.ClearBuffer();

        var bytes = new byte[frames * 8];
        for (int i = 0; i < frames; i++)
        {
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 8), mono[i]);
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 8 + 4), mono[i]);
        }
        buf.AddSamples(bytes, 0, bytes.Length);
    }

    // ----------------------------------------------------------------

    public void Stop()
    {
        var a = asio; asio = null;
        if (a != null)
        {
            a.AudioAvailable -= OnAsioAudio;
            try { a.Stop(); } catch { }
            try { a.Dispose(); } catch { }
        }
        var c = capture; capture = null;
        if (c != null)
        {
            c.DataAvailable -= OnWasapiData;
            try { c.StopRecording(); } catch { }
            try { c.Dispose(); } catch { }
        }
        var o = wasOut; wasOut = null;
        if (o != null) { try { o.Stop(); } catch { } try { o.Dispose(); } catch { } }
        buffered = null;
        Rig = null;
        Description = "";
    }

    public void Dispose() => Stop();

    sealed class SilenceProvider : IWaveProvider
    {
        public SilenceProvider(WaveFormat wf) { WaveFormat = wf; }
        public WaveFormat WaveFormat { get; }
        public int Read(byte[] buffer, int offset, int count) { Array.Clear(buffer, offset, count); return count; }
    }
}
