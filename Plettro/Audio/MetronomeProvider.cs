using NAudio.Wave;

namespace Plettro.Audio;

public enum AccentLevel { Accent = 0, Normal = 1, Mute = 2 }

public enum ClickSound { Beep = 0, Wood = 1, Click = 2, HiHat = 3, Ride = 4, Kit = 5 }

public sealed class SoundSet
{
    public required float[] Accent;
    public required float[] Normal;
    public required float[] Sub;
    /// <summary>Optional sound for beats 2, 4, ... (drum kit: snare).</summary>
    public float[]? Backbeat;
}

public struct BeatEvent
{
    public long Id;
    public int Run;
    public long Frame;
    public int Beat;
    public int Sub;
    public long Bar;
    public bool Muted;
}

/// <summary>
/// Sample-accurate metronome rendered directly in the audio thread.
/// Supports accents, subdivisions, a speed trainer and muted "gap" bars.
/// </summary>
public sealed class MetronomeProvider : ISampleProvider
{
    public const int MaxBeats = 12;

    int sampleRate = 48000;
    public WaveFormat WaveFormat { get; private set; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

    // --- settings (written by UI thread, read by audio thread) ---
    double bpm = 100;
    public double Bpm { get => Volatile.Read(ref bpm); set => Volatile.Write(ref bpm, Math.Clamp(value, 20, 400)); }
    public int BeatsPerBar { get; set; } = 4;
    public int Subdivision { get; set; } = 1;
    public float Volume { get; set; } = 0.8f;
    public ClickSound Sound
    {
        get => sound;
        set { sound = value; sounds = GetSounds(sampleRate, value); }
    }
    public AccentLevel[] Accents { get; } = Enumerable.Range(0, MaxBeats).Select(i => i == 0 ? AccentLevel.Accent : AccentLevel.Normal).ToArray();

    public bool TrainerEnabled { get; set; }
    public double TrainerTarget { get; set; } = 140;
    public double TrainerStep { get; set; } = 5;
    public int TrainerEveryBars { get; set; } = 4;

    public bool GapEnabled { get; set; }
    public int GapPlayBars { get; set; } = 3;
    public int GapMuteBars { get; set; } = 1;

    // --- state ---
    volatile bool running;
    volatile bool startRequested, stopRequested;
    volatile bool previewRequested;
    ClickSound sound = ClickSound.Kit;
    volatile SoundSet? sounds;
    readonly Dictionary<(int, ClickSound), SoundSet> soundCache = new();
    long frameCounter;
    double nextTick;
    int subIndex;
    long bar;
    int run;

    struct Voice { public float[] Data; public int Pos; public float Gain; }
    readonly List<Voice> voices = new();

    readonly object evLock = new();
    readonly BeatEvent[] events = new BeatEvent[128];
    long eventCount;

    public bool IsRunning => running || startRequested;
    public int CurrentRun => Volatile.Read(ref run);

    public void Configure(int rate)
    {
        sampleRate = rate;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(rate, 2);
        running = false;
        startRequested = stopRequested = false;
        frameCounter = 0;
        voices.Clear();
        sounds = GetSounds(rate, sound);
    }

    /// <summary>Plays the accent sound once (used when choosing a sound while stopped).</summary>
    public void Preview() => previewRequested = true;

    SoundSet GetSounds(int sr, ClickSound s)
    {
        lock (soundCache)
        {
            if (!soundCache.TryGetValue((sr, s), out var set))
            {
                set = BuildSounds(sr, s);
                soundCache[(sr, s)] = set;
            }
            return set;
        }
    }

    public void Start() { stopRequested = false; startRequested = true; }
    public void Stop() { startRequested = false; stopRequested = true; }

    public bool TryGetLatest(long playedFrame, out BeatEvent ev)
    {
        int currentRun = CurrentRun;
        lock (evLock)
        {
            long min = Math.Max(0, eventCount - events.Length);
            for (long k = eventCount - 1; k >= min; k--)
            {
                var e = events[k % events.Length];
                if (e.Run != currentRun) break;
                if (e.Frame <= playedFrame) { ev = e; return true; }
            }
        }
        ev = default;
        return false;
    }

    static SoundSet BuildSounds(int sr, ClickSound sound)
    {
        switch (sound)
        {
            case ClickSound.HiHat:
                return new SoundSet
                {
                    Accent = DrumSynth.HiHatOpen(sr, 0.95),
                    Normal = DrumSynth.HiHatClosed(sr, 0.85),
                    Sub = DrumSynth.HiHatClosed(sr, 0.45, seed: 9),
                };
            case ClickSound.Ride:
                return new SoundSet
                {
                    Accent = DrumSynth.RideBell(sr, 0.95),
                    Normal = DrumSynth.Ride(sr, 0.8),
                    Sub = DrumSynth.Ride(sr, 0.42, seed: 8),
                };
            case ClickSound.Kit:
            {
                var hat = DrumSynth.HiHatClosed(sr, 1.0);
                var kick = DrumSynth.Kick(sr, 1.0);
                return new SoundSet
                {
                    Accent = DrumSynth.Combine(DrumSynth.Crash(sr, 1.0), 0.5, kick, 0.62),
                    Normal = DrumSynth.Combine(hat, 0.42, kick, 0.6),
                    Backbeat = DrumSynth.Combine(hat, 0.38, DrumSynth.Snare(sr, 1.0), 0.66),
                    Sub = DrumSynth.HiHatClosed(sr, 0.4, seed: 9),
                };
            }
            case ClickSound.Wood:
                return new SoundSet
                {
                    Accent = Synth(sr, 0.09, t => Wood(t, 1250)),
                    Normal = Synth(sr, 0.09, t => Wood(t, 880) * 0.85),
                    Sub = Synth(sr, 0.07, t => Wood(t, 660) * 0.5),
                };
            case ClickSound.Click:
            {
                var rng = new Random(7);
                var noise = Enumerable.Range(0, sr / 10).Select(_ => (float)(rng.NextDouble() * 2 - 1)).ToArray();
                return new SoundSet
                {
                    Accent = Synth(sr, 0.05, t => ClickN(t, noise, sr) * 1.0 + Math.Sin(2 * Math.PI * 3200 * t) * Math.Exp(-t / 0.004) * 0.5),
                    Normal = Synth(sr, 0.05, t => ClickN(t, noise, sr) * 0.8),
                    Sub = Synth(sr, 0.04, t => ClickN(t, noise, sr) * 0.4),
                };
            }
            default:
                return new SoundSet
                {
                    Accent = Synth(sr, 0.08, t => Beep(t, 1760)),
                    Normal = Synth(sr, 0.08, t => Beep(t, 1320) * 0.8),
                    Sub = Synth(sr, 0.06, t => Beep(t, 990) * 0.45),
                };
        }
    }

    static double Beep(double t, double f) => Math.Sin(2 * Math.PI * f * t) * Math.Exp(-t / 0.018) * Attack(t);

    static double Wood(double t, double f) =>
        (Math.Sin(2 * Math.PI * f * t) * Math.Exp(-t / 0.012)
        + 0.45 * Math.Sin(2 * Math.PI * f * 2.76 * t) * Math.Exp(-t / 0.005)) * Attack(t) * 0.9;

    static double ClickN(double t, float[] noise, int sr)
    {
        int i = (int)(t * sr);
        if (i <= 0 || i >= noise.Length) return 0;
        return (noise[i] - noise[i - 1]) * 0.5 * Math.Exp(-t / 0.0025);
    }

    static double Attack(double t) => Math.Min(1, t / 0.0008);

    static float[] Synth(int sr, double dur, Func<double, double> f)
    {
        var data = new float[(int)(sr * dur)];
        for (int i = 0; i < data.Length; i++)
        {
            double t = (double)i / sr;
            double fade = Math.Min(1, (data.Length - i) / (sr * 0.005)); // 5 ms fade-out
            data[i] = (float)(f(t) * fade);
        }
        return data;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var set = sounds;
        if (previewRequested)
        {
            previewRequested = false;
            if (set != null && !running) voices.Add(new Voice { Data = set.Accent, Pos = 0, Gain = 1f });
        }

        if (startRequested)
        {
            startRequested = false;
            running = true;
            Interlocked.Increment(ref run);
            nextTick = frameCounter + sampleRate * 0.03; // small lead-in
            subIndex = 0;
            bar = 0;
        }
        if (stopRequested)
        {
            stopRequested = false;
            running = false;
        }

        int frames = count / 2;
        float vol = Volume;
        for (int i = 0; i < frames; i++)
        {
            long f = frameCounter + i;
            if (running && f >= nextTick)
            {
                Tick(f, set);
                nextTick += 60.0 / Bpm / Math.Max(1, Subdivision) * sampleRate;
            }

            float s = 0;
            for (int v = voices.Count - 1; v >= 0; v--)
            {
                var voice = voices[v];
                s += voice.Data[voice.Pos] * voice.Gain;
                voice.Pos++;
                if (voice.Pos >= voice.Data.Length) voices.RemoveAt(v);
                else voices[v] = voice;
            }
            s *= vol;
            if (s > 0.9f || s < -0.9f) s = 0.9f * MathF.Tanh(s / 0.9f); // soft limiter, never clips
            buffer[offset + 2 * i] = s;
            buffer[offset + 2 * i + 1] = s;
        }
        frameCounter += frames;
        return count;
    }

    void Tick(long frame, SoundSet? set)
    {
        int subdiv = Math.Max(1, Subdivision);
        int beats = Math.Clamp(BeatsPerBar, 1, MaxBeats);

        if (subIndex >= beats * subdiv)
        {
            subIndex = 0;
            bar++;
            if (TrainerEnabled && TrainerEveryBars > 0 && bar % TrainerEveryBars == 0)
            {
                double b = Bpm, target = TrainerTarget;
                if (b < target) Bpm = Math.Min(target, b + TrainerStep);
                else if (b > target) Bpm = Math.Max(target, b - TrainerStep);
            }
        }

        int beat = subIndex / subdiv;
        int sub = subIndex % subdiv;
        int cycle = Math.Max(1, GapPlayBars) + Math.Max(1, GapMuteBars);
        bool muted = GapEnabled && (bar % cycle) >= Math.Max(1, GapPlayBars);

        if (!muted && set != null)
        {
            float[]? sample = null;
            if (sub == 0)
            {
                var acc = Accents[beat];
                sample = acc switch
                {
                    AccentLevel.Accent => set.Accent,
                    AccentLevel.Normal => beat % 2 == 1 && set.Backbeat != null ? set.Backbeat : set.Normal,
                    _ => null,
                };
            }
            else if (Accents[beat] != AccentLevel.Mute)
            {
                sample = set.Sub;
            }

            if (sample != null && sample.Length > 0)
            {
                if (voices.Count > 16) voices.RemoveAt(0);
                voices.Add(new Voice { Data = sample, Pos = 0, Gain = 1f });
            }
        }

        lock (evLock)
        {
            events[eventCount % events.Length] = new BeatEvent
            {
                Id = eventCount,
                Run = run,
                Frame = frame,
                Beat = beat,
                Sub = sub,
                Bar = bar,
                Muted = muted,
            };
            eventCount++;
        }

        subIndex++;
    }
}
