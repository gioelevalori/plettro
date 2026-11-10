using NAudio.Wave;

namespace Plettro.Audio;

/// <summary>Reference tone generator (a soft, string-like tone with a few harmonics).</summary>
public sealed class ToneProvider : ISampleProvider
{
    int sampleRate = 48000;
    public WaveFormat WaveFormat { get; private set; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

    public float Volume { get; set; } = 0.5f;

    readonly object sync = new();
    double pendingFreq = -1, pendingDuration;
    double freq, phase;
    long remaining, total;

    public bool IsPlaying => Interlocked.Read(ref remaining) > 0;

    public void Configure(int rate)
    {
        sampleRate = rate;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(rate, 2);
        remaining = 0;
    }

    public void Play(double frequency, double seconds = 3.0)
    {
        lock (sync) { pendingFreq = frequency; pendingDuration = seconds; }
    }

    public void StopTone()
    {
        lock (sync) { pendingFreq = 0; pendingDuration = 0; }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        lock (sync)
        {
            if (pendingFreq >= 0)
            {
                freq = pendingFreq;
                total = (long)(pendingDuration * sampleRate);
                Interlocked.Exchange(ref remaining, total);
                phase = 0;
                pendingFreq = -1;
            }
        }

        int frames = count / 2;
        long rem = remaining;
        if (rem <= 0 || freq <= 0)
        {
            Array.Clear(buffer, offset, count);
            return count;
        }

        double inc = 2 * Math.PI * freq / sampleRate;
        float vol = Volume;
        for (int i = 0; i < frames; i++)
        {
            float s = 0;
            if (rem > 0)
            {
                long pos = total - rem;
                double t = (double)pos / sampleRate;
                double env = Math.Min(1, t / 0.01) * Math.Exp(-t / 2.2) * Math.Min(1, rem / (sampleRate * 0.08));
                double v = Math.Sin(phase) + 0.5 * Math.Sin(2 * phase) + 0.28 * Math.Sin(3 * phase) + 0.14 * Math.Sin(4 * phase);
                s = (float)(v * env * 0.32 * vol);
                phase += inc;
                if (phase > 2 * Math.PI * 1000) phase -= 2 * Math.PI * 1000;
                rem--;
            }
            buffer[offset + 2 * i] = s;
            buffer[offset + 2 * i + 1] = s;
        }
        Interlocked.Exchange(ref remaining, rem);
        return count;
    }
}
