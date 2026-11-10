using System.Numerics;

namespace Plettro.Audio;

/// <summary>
/// YIN pitch detector (de Cheveigne &amp; Kawahara, 2002) with SIMD difference function
/// and parabolic interpolation for sub-sample accuracy.
/// </summary>
public sealed class PitchDetector
{
    readonly int sampleRate;
    readonly int half;
    readonly int tauMin;
    readonly int tauMax;
    readonly float[] d;

    public double Threshold { get; set; } = 0.12;

    public int Size { get; }

    /// <summary>Diagnostic: normalized difference value at a lag (after the last Detect call).</summary>
    public float CmndfAt(int tau) => tau >= 0 && tau < d.Length ? d[tau] : 1f;

    public PitchDetector(int sampleRate, int size, double minFreq, double maxFreq)
    {
        this.sampleRate = sampleRate;
        Size = size;
        half = size / 2;
        tauMin = Math.Max(2, (int)(sampleRate / maxFreq));
        tauMax = Math.Min(half - 2, (int)Math.Ceiling(sampleRate / minFreq));
        d = new float[tauMax + 2];
    }

    /// <summary>Returns (frequency in Hz, clarity 0..1). Frequency 0 means no pitch.</summary>
    public (double Frequency, double Clarity) Detect(float[] x)
    {
        int vs = Vector<float>.Count;

        // Difference function d(tau) = sum_j (x[j] - x[j+tau])^2, j < half
        for (int tau = 1; tau <= tauMax + 1; tau++)
        {
            var acc = Vector<float>.Zero;
            int j = 0;
            for (; j <= half - vs; j += vs)
            {
                var diff = new Vector<float>(x, j) - new Vector<float>(x, j + tau);
                acc += diff * diff;
            }
            float sum = Vector.Dot(acc, Vector<float>.One);
            for (; j < half; j++)
            {
                float diff = x[j] - x[j + tau];
                sum += diff * diff;
            }
            d[tau] = sum;
        }

        // Cumulative mean normalized difference
        d[0] = 1;
        double running = 0;
        for (int tau = 1; tau <= tauMax + 1; tau++)
        {
            running += d[tau];
            d[tau] = running > 0 ? (float)(d[tau] * tau / running) : 1f;
        }

        // Global minimum: tells how periodic the signal is at all
        float globalMin = float.MaxValue;
        for (int tau = tauMin; tau <= tauMax; tau++)
            if (d[tau] < globalMin) globalMin = d[tau];
        if (globalMin > 0.35f) return (0, 0);

        // First dip that is "good enough" compared with the best one.
        // Taking the global minimum directly causes sub-octave errors (period multiples
        // look equally good, especially with real, slightly inharmonic strings).
        double threshold = Math.Max(Threshold, globalMin + 0.08);
        int best = -1;
        for (int tau = tauMin; tau <= tauMax; tau++)
        {
            if (d[tau] < threshold)
            {
                while (tau + 1 <= tauMax && d[tau + 1] < d[tau]) tau++;
                best = tau;
                break;
            }
        }
        if (best < 0) return (0, 0);

        // Parabolic interpolation
        double s0 = d[best - 1], s1 = d[best], s2 = d[best + 1];
        double denom = s0 + s2 - 2 * s1;
        double shift = Math.Abs(denom) > 1e-12 ? 0.5 * (s0 - s2) / denom : 0;
        shift = Math.Clamp(shift, -1, 1);

        double period = best + shift;
        if (period <= 0) return (0, 0);
        return (sampleRate / period, Math.Clamp(1 - d[best], 0, 1));
    }
}

