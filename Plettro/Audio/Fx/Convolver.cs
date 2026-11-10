using System.Numerics;
using NAudio.Wave;

namespace Plettro.Audio.Fx;

/// <summary>
/// Direct-form (SIMD) convolution for cabinet impulse responses.
/// The IR is truncated to MaxTaps, which keeps the cost predictable and covers the
/// part of a cab IR that matters for the tone.
/// </summary>
public sealed class Convolver
{
    public const int MaxTaps = 2048;

    readonly float[] taps;     // reversed IR
    readonly float[] history;  // doubled ring buffer so a window is always contiguous
    int pos;
    readonly int n;

    public string Name { get; }

    public Convolver(float[] ir, string name)
    {
        Name = name;
        n = Math.Min(ir.Length, MaxTaps);
        int vs = Vector<float>.Count;
        n = (n + vs - 1) / vs * vs;
        taps = new float[n];
        for (int i = 0; i < Math.Min(ir.Length, n); i++) taps[n - 1 - i] = ir[i];
        // fade the truncated tail
        int fade = Math.Min(256, n / 4);
        for (int i = 0; i < fade; i++) taps[i] *= (float)i / fade;
        history = new float[2 * n];
    }

    public float Process(float x)
    {
        history[pos] = x;
        history[pos + n] = x;
        pos++;
        if (pos >= n) pos = 0;
        // window = the n most recent samples, oldest first, starting at pos
        int vs = Vector<float>.Count;
        var acc = Vector<float>.Zero;
        for (int i = 0; i < n; i += vs)
            acc += new Vector<float>(history, pos + i) * new Vector<float>(taps, i);
        return Vector.Dot(acc, Vector<float>.One);
    }

    /// <summary>Loads a WAV IR, resamples it (linear) to the target rate and normalizes it.</summary>
    public static Convolver Load(string path, int sampleRate)
    {
        using var file = new WaveFileReader(path);
        var reader = file.ToSampleProvider();
        int channels = reader.WaveFormat.Channels;
        var all = new List<float>();
        var buf = new float[4096 * channels];
        int read;
        while ((read = reader.Read(buf, 0, buf.Length)) > 0 && all.Count < 400000)
            for (int i = 0; i < read; i += channels) all.Add(buf[i]);

        double ratio = (double)reader.WaveFormat.SampleRate / sampleRate;
        int len = Math.Min(MaxTaps, (int)(all.Count / ratio));
        var ir = new float[len];
        for (int i = 0; i < len; i++)
        {
            double p = i * ratio;
            int a = (int)p;
            double f = p - a;
            float s0 = a < all.Count ? all[a] : 0, s1 = a + 1 < all.Count ? all[a + 1] : 0;
            ir[i] = (float)(s0 + (s1 - s0) * f);
        }
        // normalize to unity energy so different IRs have similar loudness
        double energy = ir.Sum(v => (double)v * v);
        if (energy > 0)
        {
            float k = (float)(1 / Math.Sqrt(energy));
            for (int i = 0; i < len; i++) ir[i] *= k;
        }
        return new Convolver(ir, System.IO.Path.GetFileNameWithoutExtension(path));
    }
}
