using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;

namespace Plettro.Audio;

public enum ChannelMode { Auto = 0, Left = 1, Right = 2, Mix = 3 }

public readonly record struct PitchReading(double Frequency, double Clarity, double LevelDb, DateTime TimeUtc);

/// <summary>
/// Captures audio from a WASAPI input (e.g. Scarlett Solo "Analogue 1 + 2"),
/// picks a channel, and runs pitch detection continuously.
/// </summary>
public sealed class InputMonitor : IDisposable
{
    static readonly Guid FloatSubtype = new("00000003-0000-0010-8000-00aa00389b71");

    const int WindowSize = 4096;
    const int Hop = 2048;
    const double MinFreq = 28;
    const double MaxFreq = 1400;

    readonly object sync = new();
    WasapiCapture? capture;

    float[] ring = new float[WindowSize];
    readonly float[] work = new float[WindowSize];
    int writePos, filled, sinceDetect;
    PitchDetector? detector;
    BiQuadFilter? lowpass;
    int decim = 1, decimCount;
    float decimAcc;
    readonly List<double> history = new();

    bool isFloat;
    int bytesPerSample, channels, blockAlign;
    double energyL, energyR;
    int activeChannel = 1;

    float peak;
    long lastClipTicks;
    PitchReading latest;

    public ChannelMode Mode { get; set; } = ChannelMode.Auto;
    public double GateDb { get; set; } = -55;
    public int SampleRate { get; private set; }
    public int Channels => channels;
    /// <summary>0 = input 1 (left), 1 = input 2 (right), -1 = mix.</summary>
    public int ActiveChannel => Mode == ChannelMode.Mix || channels < 2 ? (channels < 2 ? 0 : -1) : activeChannel;
    public bool IsRunning => capture != null;

    public float Peak => Volatile.Read(ref peak);
    public DateTime LastClipUtc => new(Interlocked.Read(ref lastClipTicks), DateTimeKind.Utc);

    public PitchReading Latest { get { lock (sync) return latest; } }

    public void Start(MMDevice device)
    {
        Stop();
        var c = new WasapiCapture(device, true, 20);
        var wf = c.WaveFormat;
        channels = wf.Channels;
        blockAlign = wf.BlockAlign;
        bytesPerSample = wf.BitsPerSample / 8;
        isFloat = wf.Encoding == WaveFormatEncoding.IeeeFloat
                  || (wf is WaveFormatExtensible ext && ext.SubFormat == FloatSubtype);
        SampleRate = wf.SampleRate;

        decim = Math.Max(1, (int)Math.Round(wf.SampleRate / 48000.0));
        int effectiveRate = wf.SampleRate / decim;
        detector = new PitchDetector(effectiveRate, WindowSize, MinFreq, MaxFreq);
        lowpass = BiQuadFilter.LowPassFilter(effectiveRate, 1200, 0.707f);
        ring = new float[WindowSize];
        writePos = filled = sinceDetect = decimCount = 0;
        decimAcc = 0;
        history.Clear();
        activeChannel = channels > 1 ? 1 : 0; // Scarlett Solo: guitar goes into input 2 (right)

        c.DataAvailable += OnData;
        c.StartRecording();
        capture = c;
    }

    public void Stop()
    {
        var c = capture;
        capture = null;
        if (c == null) return;
        c.DataAvailable -= OnData;
        try { c.StopRecording(); } catch { /* device may be gone */ }
        try { c.Dispose(); } catch { }
        lock (sync) latest = default;
        Volatile.Write(ref peak, 0f);
    }

    float ReadSample(byte[] b, int o)
    {
        if (isFloat && bytesPerSample == 4) return BitConverter.ToSingle(b, o);
        return bytesPerSample switch
        {
            2 => BitConverter.ToInt16(b, o) / 32768f,
            3 => ((b[o] | (b[o + 1] << 8) | ((sbyte)b[o + 2] << 16))) / 8388608f,
            4 => BitConverter.ToInt32(b, o) / 2147483648f,
            _ => 0f,
        };
    }

    void OnData(object? sender, WaveInEventArgs e)
    {
        if (detector == null || lowpass == null) return;
        var buf = e.Buffer;
        int frames = e.BytesRecorded / blockAlign;
        if (frames <= 0) return;

        // Pass 1: channel energies (for automatic input selection)
        if (channels > 1 && Mode == ChannelMode.Auto)
        {
            double eL = 0, eR = 0;
            for (int i = 0; i < frames; i++)
            {
                int o = i * blockAlign;
                float l = ReadSample(buf, o), r = ReadSample(buf, o + bytesPerSample);
                eL += l * l; eR += r * r;
            }
            energyL = energyL * 0.8 + eL / frames * 0.2;
            energyR = energyR * 0.8 + eR / frames * 0.2;
            const double minEnergy = 1e-7; // ~ -70 dBFS
            if (activeChannel == 0 && energyR > energyL * 4 && energyR > minEnergy) activeChannel = 1;
            else if (activeChannel == 1 && energyL > energyR * 4 && energyL > minEnergy) activeChannel = 0;
        }

        float bufPeak = 0;
        for (int i = 0; i < frames; i++)
        {
            int o = i * blockAlign;
            float s;
            if (channels < 2) s = ReadSample(buf, o);
            else
            {
                switch (Mode)
                {
                    case ChannelMode.Left: s = ReadSample(buf, o); break;
                    case ChannelMode.Right: s = ReadSample(buf, o + bytesPerSample); break;
                    case ChannelMode.Mix: s = 0.5f * (ReadSample(buf, o) + ReadSample(buf, o + bytesPerSample)); break;
                    default: s = ReadSample(buf, o + activeChannel * bytesPerSample); break;
                }
            }

            float a = Math.Abs(s);
            if (a > bufPeak) bufPeak = a;

            decimAcc += s;
            if (++decimCount < decim) continue;
            float ds = lowpass.Transform(decimAcc / decim);
            decimAcc = 0; decimCount = 0;

            ring[writePos] = ds;
            writePos = (writePos + 1) % WindowSize;
            if (filled < WindowSize) filled++;
            if (++sinceDetect >= Hop && filled >= WindowSize)
            {
                sinceDetect = 0;
                Analyze();
            }
        }

        Volatile.Write(ref peak, bufPeak);
        if (bufPeak >= 0.989f) Interlocked.Exchange(ref lastClipTicks, DateTime.UtcNow.Ticks);
    }

    void Analyze()
    {
        // Unroll ring (oldest first)
        int first = WindowSize - writePos;
        Array.Copy(ring, writePos, work, 0, first);
        Array.Copy(ring, 0, work, first, writePos);

        double sumSq = 0;
        for (int i = 0; i < WindowSize; i++) sumSq += work[i] * work[i];
        double rms = Math.Sqrt(sumSq / WindowSize);
        double levelDb = rms > 0 ? 20 * Math.Log10(rms) : -120;
        var now = DateTime.UtcNow;

        if (levelDb < GateDb)
        {
            history.Clear();
            lock (sync) latest = new PitchReading(0, 0, levelDb, now);
            return;
        }

        var (freq, clarity) = detector!.Detect(work);
        if (freq < MinFreq || freq > MaxFreq || clarity < 0.82)
        {
            lock (sync) latest = latest with { LevelDb = levelDb };
            return;
        }

        // If the new value is far from the recent history (new note), restart history
        if (history.Count > 0)
        {
            double last = history[^1];
            if (Math.Abs(1200 * Math.Log2(freq / last)) > 80) history.Clear();
        }
        history.Add(freq);
        if (history.Count > 5) history.RemoveAt(0);

        var sorted = history.ToArray();
        Array.Sort(sorted);
        double median = sorted[sorted.Length / 2];

        lock (sync) latest = new PitchReading(median, clarity, levelDb, now);
    }

    public void Dispose() => Stop();
}
