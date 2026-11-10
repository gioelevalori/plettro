namespace Plettro.Audio.Fx;

public enum AmpModel { Clean = 0, Crunch = 1, Lead = 2, Metal = 3 }

public enum CabType { Off = 0, Cab4x12 = 1, Cab2x12 = 2, Cab1x12 = 3, ImpulseResponse = 4 }

/// <summary>
/// Real-time guitar chain: noise gate → overdrive pedal → amp (oversampled tube-style
/// preamp, tone stack, power amp) → cabinet (filters or IR) → delay → reverb → output.
/// Parameters can be changed from any thread; filters are recomputed at the start of a block.
/// </summary>
public sealed class GuitarRig
{
    readonly double fs;
    volatile bool dirty = true;

    // ---------------- parameters (0..10 knobs unless noted) ----------------
    public bool GateOn { get; set; } = true;
    public double GateThresholdDb { get; set; } = -60;

    public bool DriveOn { get; set; }
    public double Drive { get; set; } = 4;
    public double DriveTone { get; set; } = 5;
    public double DriveLevel { get; set; } = 6;

    public AmpModel Model { get => model; set { model = value; dirty = true; } }
    public double Gain { get; set; } = 5;
    public double Bass { get => bass; set { bass = value; dirty = true; } }
    public double Mid { get => mid; set { mid = value; dirty = true; } }
    public double Treble { get => treble; set { treble = value; dirty = true; } }
    public double Presence { get => presence; set { presence = value; dirty = true; } }
    public double Master { get; set; } = 5;

    public CabType Cab { get => cab; set { cab = value; dirty = true; } }
    public Convolver? ImpulseResponse { get; set; }

    public bool DelayOn { get; set; }
    public double DelayMs { get; set; } = 380;
    public double DelayFeedback { get; set; } = 3;
    public double DelayMix { get; set; } = 3;

    public bool ReverbOn { get; set; } = true;
    public double ReverbSize { get; set; } = 5;
    public double ReverbMix { get; set; } = 2;

    public double OutputDb { get; set; } = 0;

    AmpModel model = AmpModel.Crunch;
    double bass = 5, mid = 5, treble = 5, presence = 5;
    CabType cab = CabType.Cab4x12;

    // ---------------- state ----------------
    double gateEnv, gateGain; // starts closed
    int gateHold;
    Biquad driveHp, driveTone, driveMidHump;
    Biquad ampTight, ampPreEq, ampInterHp, ampInterLp;
    Biquad toneBass, toneMid, toneTreble, tonePresence;
    Biquad cab1, cab2, cab3, cab4, cab5;
    readonly Oversampler os1, os2;
    double gainSmooth = 1, masterSmooth = 1;

    readonly float[] delayBuf;
    int delayPos;
    Biquad delayLp;
    readonly Reverb reverb;

    public float PeakOut { get; private set; }
    public float PeakIn { get; private set; }

    // ---------------- noise calibration ----------------
    int calibrateLeft;
    float calibratePeak;
    double calibratedNoiseDb = double.NaN;

    /// <summary>Measured input noise peak (dBFS) after a calibration, NaN while unknown.</summary>
    public double CalibratedNoiseDb => Volatile.Read(ref calibratedNoiseDb);
    public bool IsCalibrating => Volatile.Read(ref calibrateLeft) > 0;

    /// <summary>Listens to the input for a while (output muted) to measure hum/noise.</summary>
    public void StartCalibration(double seconds = 1.5)
    {
        calibratePeak = 0;
        Volatile.Write(ref calibratedNoiseDb, double.NaN);
        Volatile.Write(ref calibrateLeft, (int)(seconds * fs));
    }

    public GuitarRig(double sampleRate)
    {
        fs = sampleRate;
        os1 = new Oversampler(sampleRate);
        os2 = new Oversampler(sampleRate);
        delayBuf = new float[(int)(sampleRate * 2.1)];
        reverb = new Reverb(sampleRate);
    }

    void UpdateFilters()
    {
        dirty = false;
        driveHp.HighPass(fs, 720, 0.6);
        driveMidHump.Peak(fs, 900, 0.8, 4);
        driveTone.LowPass(fs, 900 + DriveTone * 550);

        switch (model)
        {
            case AmpModel.Clean:
                ampTight.HighPass(fs, 70); ampPreEq.Peak(fs, 2500, 0.7, 2); break;
            case AmpModel.Crunch:
                ampTight.HighPass(fs, 90); ampPreEq.Peak(fs, 1000, 0.8, 3); break;
            case AmpModel.Lead:
                ampTight.HighPass(fs, 110); ampPreEq.Peak(fs, 850, 0.7, 5); break;
            default: // Metal
                ampTight.HighPass(fs, 160); ampPreEq.Peak(fs, 1400, 0.9, 4); break;
        }
        ampInterHp.HighPass(fs, 40);
        ampInterLp.LowPass(fs, model == AmpModel.Metal ? 7000 : 6000);

        double midCenter = model == AmpModel.Metal ? 650 : 750;
        double midBias = model == AmpModel.Metal ? -3 : 0;
        toneBass.LowShelf(fs, 140, (bass - 5) * 2.4);
        toneMid.Peak(fs, midCenter, 0.6, (mid - 5) * 2.4 + midBias);
        toneTreble.HighShelf(fs, 2600, (treble - 5) * 2.4);
        tonePresence.HighShelf(fs, 5000, (presence - 5) * 1.8);

        switch (cab)
        {
            case CabType.Cab4x12:
                cab1.HighPass(fs, 80, 0.9); cab2.Peak(fs, 115, 1.4, 4); cab3.Peak(fs, 2400, 1.2, 3);
                cab4.LowPass(fs, 4600, 0.8); cab5.LowPass(fs, 5200, 1.1); break;
            case CabType.Cab2x12:
                cab1.HighPass(fs, 90, 0.8); cab2.Peak(fs, 140, 1.2, 2.5); cab3.Peak(fs, 2000, 1.0, 2.5);
                cab4.LowPass(fs, 5200, 0.8); cab5.LowPass(fs, 6000, 1.0); break;
            case CabType.Cab1x12:
                cab1.HighPass(fs, 110, 0.7); cab2.Peak(fs, 180, 1.0, 1.5); cab3.Peak(fs, 1700, 0.9, 3);
                cab4.LowPass(fs, 5600, 0.75); cab5.LowPass(fs, 7000, 0.9); break;
        }
        delayLp.LowPass(fs, 3800);
    }

    double ModelGain()
    {
        // knob 0..10 → linear gain, exponential per model
        double g = Gain / 10;
        return model switch
        {
            AmpModel.Clean => 1.5 * Math.Pow(8, g),      // 1.5 .. 12
            AmpModel.Crunch => 4 * Math.Pow(15, g),      // 4 .. 60
            AmpModel.Lead => 12 * Math.Pow(20, g),       // 12 .. 240
            _ => 25 * Math.Pow(24, g),                   // 25 .. 600
        };
    }

    double ModelTrim() => model switch
    {
        AmpModel.Clean => 1.8,
        AmpModel.Crunch => 0.55,
        AmpModel.Lead => 0.4,
        _ => 0.5,
    };

    /// <summary>Processes a mono block in place.</summary>
    public void Process(float[] x, int count)
    {
        if (dirty) UpdateFilters();

        var m = model;
        bool twoStages = m is AmpModel.Lead or AmpModel.Metal;
        double targetGain = ModelGain();
        double targetMaster = Math.Pow(10, (Master - 5) / 10.0); // ±10 dB-ish
        double trim = ModelTrim();
        double outGain = Math.Pow(10, OutputDb / 20);

        double gateThr = Math.Pow(10, GateThresholdDb / 20);
        double envRelease = Math.Exp(-1 / (0.05 * fs));
        double gateOpen = 1 - Math.Exp(-1 / (0.001 * fs));
        double gateClose = 1 - Math.Exp(-1 / (0.012 * fs));
        int holdSamples = (int)(0.05 * fs);
        double smooth = 1 - Math.Exp(-1 / (0.02 * fs));

        double driveGain = 2 + Drive * 6;
        double driveLevel = DriveLevel / 6;
        var conv = Cab == CabType.ImpulseResponse ? ImpulseResponse : null;
        bool cabSim = Cab is CabType.Cab4x12 or CabType.Cab2x12 or CabType.Cab1x12;

        int delaySamples = Math.Clamp((int)(DelayMs / 1000 * fs), 1, delayBuf.Length - 1);
        float fb = (float)(DelayFeedback / 10 * 0.85), dmix = (float)(DelayMix / 10);
        reverb.SetSize(ReverbSize / 10);
        float rmix = (float)(ReverbMix / 10 * 0.6);

        float peakIn = 0, peakOut = 0;
        for (int i = 0; i < count; i++)
        {
            float s = x[i];
            float a = Math.Abs(s);
            if (a > peakIn) peakIn = a;

            if (calibrateLeft > 0)
            {
                // ignore the first 150 ms (switch-on transients)
                if (calibrateLeft < (int)(fs * 1.35) && a > calibratePeak) calibratePeak = a;
                if (--calibrateLeft == 0)
                    Volatile.Write(ref calibratedNoiseDb, 20 * Math.Log10(Math.Max(calibratePeak, 1e-6)));
                x[i] = 0;
                continue;
            }
            // --- noise gate (applied to the input, follows the input envelope)
            if (GateOn)
            {
                gateEnv = Math.Max(a, gateEnv * envRelease);
                // hysteresis (opens above the threshold, closes 6 dB below it) + 50 ms hold
                bool open = gateGain > 0.5;
                double target;
                if (gateEnv > (open ? gateThr * 0.5 : gateThr)) { target = 1; gateHold = holdSamples; }
                else if (gateHold > 0) { gateHold--; target = open ? 1 : 0; }
                else target = 0;
                gateGain += (target - gateGain) * (target > gateGain ? gateOpen : gateClose);
                s *= (float)gateGain;
            }

            // --- overdrive pedal (mid-hump soft clipper)
            if (DriveOn)
            {
                float hp = driveHp.Process(s);
                float d = driveMidHump.Process(s + hp * (float)(driveGain * 0.6));
                d = MathF.Tanh(d * (float)driveGain * 0.5f);
                s = driveTone.Process(d) * (float)driveLevel * 0.5f;
            }

            // --- amp
            gainSmooth += (targetGain - gainSmooth) * smooth;
            masterSmooth += (targetMaster - masterSmooth) * smooth;

            float pre = ampPreEq.Process(ampTight.Process(s)) * (float)gainSmooth;
            float y = os1.Process(pre, Stage1);
            if (twoStages)
            {
                y = ampInterLp.Process(ampInterHp.Process(y));
                y = os2.Process(y * (m == AmpModel.Metal ? 6f : 4f), Stage2);
            }

            y = toneBass.Process(y);
            y = toneMid.Process(y);
            y = toneTreble.Process(y);
            y = tonePresence.Process(y);

            // power amp: gentle saturation
            y = MathF.Tanh(y * (float)masterSmooth * 0.9f) * (float)trim;

            // --- cabinet
            if (cabSim)
                y = cab5.Process(cab4.Process(cab3.Process(cab2.Process(cab1.Process(y)))));
            else if (conv != null)
                y = conv.Process(y) * 0.5f;

            // --- delay
            if (DelayOn)
            {
                int rp = delayPos - delaySamples;
                if (rp < 0) rp += delayBuf.Length;
                float echo = delayBuf[rp];
                delayBuf[delayPos] = y + delayLp.Process(echo) * fb;
                y += echo * dmix;
            }
            else delayBuf[delayPos] = 0;
            if (++delayPos >= delayBuf.Length) delayPos = 0;

            // --- reverb
            if (ReverbOn)
            {
                float wet = reverb.Process(y);
                y = y * (1 - rmix * 0.4f) + wet * rmix;
            }

            y *= (float)outGain;
            // safety limiter
            if (y > 0.95f || y < -0.95f) y = 0.95f * MathF.Tanh(y / 0.95f);
            float ay = Math.Abs(y);
            if (ay > peakOut) peakOut = ay;
            x[i] = y;
        }
        PeakIn = peakIn;
        PeakOut = peakOut;
    }

    // Asymmetric tube-like curves (bias adds even harmonics)
    static float Stage1(float v) => MathF.Tanh(v + 0.15f) - 0.1489f;
    static float Stage2(float v) => MathF.Tanh(v - 0.1f) + 0.0997f;

    /// <summary>4× oversampling around a static nonlinearity (reduces aliasing at high gain).</summary>
    sealed class Oversampler
    {
        Biquad up1, up2, dn1, dn2;

        public Oversampler(double fs)
        {
            double f = fs * 4, cut = Math.Min(18000, fs * 0.42);
            up1.LowPass(f, cut, 0.5412); up2.LowPass(f, cut, 1.3066);
            dn1.LowPass(f, cut, 0.5412); dn2.LowPass(f, cut, 1.3066);
        }

        public float Process(float x, Func<float, float> shaper)
        {
            float outSample = 0;
            for (int k = 0; k < 4; k++)
            {
                float u = up2.Process(up1.Process(k == 0 ? x * 4 : 0));
                float v = shaper(u);
                float d = dn2.Process(dn1.Process(v));
                if (k == 3) outSample = d;
            }
            return outSample;
        }
    }
}

/// <summary>Mono Freeverb-style reverb (8 damped combs + 4 allpasses).</summary>
public sealed class Reverb
{
    readonly Comb[] combs;
    readonly Allpass[] allpasses;

    public Reverb(double fs)
    {
        double k = fs / 44100;
        combs = new[] { 1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617 }.Select(n => new Comb((int)(n * k))).ToArray();
        allpasses = new[] { 556, 441, 341, 225 }.Select(n => new Allpass((int)(n * k))).ToArray();
    }

    public void SetSize(double size)
    {
        float fb = (float)(0.7 + 0.28 * Math.Clamp(size, 0, 1));
        foreach (var c in combs) { c.Feedback = fb; c.Damp = 0.25f; }
    }

    public float Process(float x)
    {
        float input = x * 0.015f;
        float o = 0;
        foreach (var c in combs) o += c.Process(input);
        foreach (var a in allpasses) o = a.Process(o);
        return o;
    }

    sealed class Comb
    {
        readonly float[] buf; int i; float store;
        public float Feedback = 0.84f, Damp = 0.2f;
        public Comb(int n) { buf = new float[Math.Max(1, n)]; }
        public float Process(float x)
        {
            float o = buf[i];
            store = o * (1 - Damp) + store * Damp;
            buf[i] = x + store * Feedback;
            if (++i >= buf.Length) i = 0;
            return o;
        }
    }

    sealed class Allpass
    {
        readonly float[] buf; int i;
        public Allpass(int n) { buf = new float[Math.Max(1, n)]; }
        public float Process(float x)
        {
            float b = buf[i];
            float o = -x + b;
            buf[i] = x + b * 0.5f;
            if (++i >= buf.Length) i = 0;
            return o;
        }
    }
}
