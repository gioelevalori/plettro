namespace Plettro.Audio;

/// <summary>
/// Procedural drum sounds (no samples): cymbals use the classic "six detuned square
/// oscillators + noise, high-passed" recipe, drums use pitch-swept sines and filtered noise.
/// </summary>
public static class DrumSynth
{
    // Inharmonic oscillator ratios used by classic analog drum machines for metal sounds
    static readonly double[] MetalFreqs = { 205.3, 304.4, 369.6, 522.7, 540.0, 800.0 };

    public static float[] HiHatClosed(int sr, double gain, int seed = 1)
    {
        var x = Metal(sr, 0.16, 1.0, 0.35, seed);
        HighPass(x, sr, 7000, 0.7); HighPass(x, sr, 7000, 0.7);
        Envelope(x, sr, 0.0005, t => Math.Exp(-t / 0.022));
        return Normalize(x, gain);
    }

    public static float[] HiHatOpen(int sr, double gain, int seed = 2)
    {
        var x = Metal(sr, 0.7, 1.0, 0.4, seed);
        HighPass(x, sr, 6500, 0.7); HighPass(x, sr, 6500, 0.7);
        Envelope(x, sr, 0.0008, t => Math.Exp(-t / 0.16));
        return Normalize(x, gain);
    }

    public static float[] Ride(int sr, double gain, int seed = 3)
    {
        var metal = Metal(sr, 1.3, 1.9, 0.25, seed);
        HighPass(metal, sr, 4500, 0.7); HighPass(metal, sr, 4500, 0.7);
        // the characteristic "ping": a few high inharmonic partials
        var ping = Partials(sr, 1.3, new[] { 3150.0, 4720.0, 5310.0, 6890.0 }, new[] { 1.0, 0.7, 0.5, 0.35 }, 0.5);
        var x = Mix(metal, 0.9, ping, 0.35);
        Envelope(x, sr, 0.0006, t => 0.35 * Math.Exp(-t / 0.012) + 0.65 * Math.Exp(-t / 0.42));
        return Normalize(x, gain);
    }

    public static float[] RideBell(int sr, double gain, int seed = 4)
    {
        var ride = Ride(sr, 0.6, seed);
        var bell = Partials(sr, 1.6, new[] { 742.0, 1118.0, 1637.0, 2431.0, 3290.0 }, new[] { 1.0, 0.8, 0.55, 0.4, 0.25 }, 0.7);
        Envelope(bell, sr, 0.0005, _ => 1);
        return Normalize(Mix(ride, 1.0, bell, 0.55), gain);
    }

    public static float[] Crash(int sr, double gain, int seed = 5)
    {
        var x = Metal(sr, 2.0, 1.15, 0.9, seed);
        HighPass(x, sr, 3500, 0.7); HighPass(x, sr, 3000, 0.7);
        Envelope(x, sr, 0.0015, t => 0.3 * Math.Exp(-t / 0.05) + 0.7 * Math.Exp(-t / 0.6));
        return Normalize(x, gain);
    }

    public static float[] Kick(int sr, double gain)
    {
        int n = (int)(sr * 0.45);
        var x = new float[n];
        double phase = 0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / sr;
            double f = 48 + 110 * Math.Exp(-t / 0.035);
            phase += 2 * Math.PI * f / sr;
            double body = Math.Sin(phase) * Math.Exp(-t / 0.2);
            double click = Math.Sin(2 * Math.PI * 1400 * t) * Math.Exp(-t / 0.0025) * 0.4;
            x[i] = (float)Math.Tanh(1.6 * (body + click));
        }
        Envelope(x, sr, 0.0005, _ => 1);
        return Normalize(x, gain);
    }

    public static float[] Snare(int sr, double gain, int seed = 6)
    {
        int n = (int)(sr * 0.35);
        var rng = new Random(seed);
        var noise = new float[n];
        for (int i = 0; i < n; i++) noise[i] = (float)(rng.NextDouble() * 2 - 1);
        HighPass(noise, sr, 1800, 0.7);
        Envelope(noise, sr, 0.0005, t => Math.Exp(-t / 0.085));
        var tone = Partials(sr, 0.35, new[] { 185.0, 330.0 }, new[] { 1.0, 0.5 }, 0.06);
        return Normalize(Mix(noise, 0.75, tone, 0.6), gain);
    }

    // ---------- kits used by the metronome ----------

    public static float[] Combine(float[] a, double ga, float[] b, double gb) => Mix(a, ga, b, gb);

    // ---------- building blocks ----------

    static float[] Metal(int sr, double seconds, double pitch, double noiseMix, int seed)
    {
        int n = (int)(sr * seconds);
        var x = new float[n];
        var rng = new Random(seed);
        var phases = MetalFreqs.Select(_ => rng.NextDouble()).ToArray();
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / sr;
            double s = 0;
            for (int k = 0; k < MetalFreqs.Length; k++)
            {
                double p = (MetalFreqs[k] * pitch * t + phases[k]) % 1.0;
                s += p < 0.5 ? 1 : -1;
            }
            s = s / MetalFreqs.Length + noiseMix * (rng.NextDouble() * 2 - 1);
            x[i] = (float)s;
        }
        return x;
    }

    static float[] Partials(int sr, double seconds, double[] freqs, double[] amps, double decay)
    {
        int n = (int)(sr * seconds);
        var x = new float[n];
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / sr;
            double s = 0;
            for (int k = 0; k < freqs.Length; k++)
                s += amps[k] * Math.Sin(2 * Math.PI * freqs[k] * t) * Math.Exp(-t / (decay * (1 - k * 0.12)));
            x[i] = (float)s;
        }
        return x;
    }

    /// <summary>RBJ biquad high-pass, in place.</summary>
    static void HighPass(float[] x, int sr, double fc, double q)
    {
        fc = Math.Min(fc, sr * 0.45);
        double w0 = 2 * Math.PI * fc / sr, alpha = Math.Sin(w0) / (2 * q), c = Math.Cos(w0);
        double a0 = 1 + alpha;
        double b0 = (1 + c) / 2 / a0, b1 = -(1 + c) / a0, b2 = b0, a1 = -2 * c / a0, a2 = (1 - alpha) / a0;
        double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
        for (int i = 0; i < x.Length; i++)
        {
            double xi = x[i];
            double y = b0 * xi + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
            x2 = x1; x1 = xi; y2 = y1; y1 = y;
            x[i] = (float)y;
        }
    }

    static void Envelope(float[] x, int sr, double attack, Func<double, double> decay)
    {
        int fadeOut = (int)(sr * 0.01);
        for (int i = 0; i < x.Length; i++)
        {
            double t = (double)i / sr;
            double env = Math.Min(1, t / attack) * decay(t);
            if (i > x.Length - fadeOut) env *= (double)(x.Length - i) / fadeOut;
            x[i] = (float)(x[i] * env);
        }
    }

    static float[] Mix(float[] a, double ga, float[] b, double gb)
    {
        var x = new float[Math.Max(a.Length, b.Length)];
        for (int i = 0; i < x.Length; i++)
            x[i] = (float)((i < a.Length ? a[i] * ga : 0) + (i < b.Length ? b[i] * gb : 0));
        return x;
    }

    static float[] Normalize(float[] x, double peak)
    {
        float max = 0;
        foreach (var v in x) max = Math.Max(max, Math.Abs(v));
        if (max <= 0) return x;
        float k = (float)(peak / max);
        for (int i = 0; i < x.Length; i++) x[i] *= k;
        return x;
    }
}
