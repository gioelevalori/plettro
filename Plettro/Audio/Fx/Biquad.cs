namespace Plettro.Audio.Fx;

/// <summary>RBJ "Audio EQ Cookbook" biquad, transposed direct form II.</summary>
public struct Biquad
{
    double b0, b1, b2, a1, a2;
    double z1, z2;

    public float Process(float x)
    {
        double y = b0 * x + z1;
        z1 = b1 * x - a1 * y + z2;
        z2 = b2 * x - a2 * y;
        // flush denormals: decaying filter states would otherwise slow the CPU down a lot in silence
        if (Math.Abs(z1) < 1e-20) z1 = 0;
        if (Math.Abs(z2) < 1e-20) z2 = 0;
        return (float)y;
    }

    public void Reset() { z1 = z2 = 0; }

    void Set(double nb0, double nb1, double nb2, double a0, double na1, double na2)
    {
        b0 = nb0 / a0; b1 = nb1 / a0; b2 = nb2 / a0; a1 = na1 / a0; a2 = na2 / a0;
    }

    static (double w, double cos, double alpha) Pre(double fs, double f, double q)
    {
        f = Math.Clamp(f, 1, fs * 0.49);
        double w = 2 * Math.PI * f / fs;
        return (w, Math.Cos(w), Math.Sin(w) / (2 * q));
    }

    public void LowPass(double fs, double f, double q = 0.7071)
    {
        var (_, c, a) = Pre(fs, f, q);
        Set((1 - c) / 2, 1 - c, (1 - c) / 2, 1 + a, -2 * c, 1 - a);
    }

    public void HighPass(double fs, double f, double q = 0.7071)
    {
        var (_, c, a) = Pre(fs, f, q);
        Set((1 + c) / 2, -(1 + c), (1 + c) / 2, 1 + a, -2 * c, 1 - a);
    }

    public void Peak(double fs, double f, double q, double gainDb)
    {
        var (_, c, a) = Pre(fs, f, q);
        double A = Math.Pow(10, gainDb / 40);
        Set(1 + a * A, -2 * c, 1 - a * A, 1 + a / A, -2 * c, 1 - a / A);
    }

    public void LowShelf(double fs, double f, double gainDb, double s = 0.8)
    {
        double A = Math.Pow(10, gainDb / 40);
        double w = 2 * Math.PI * Math.Clamp(f, 1, fs * 0.49) / fs, c = Math.Cos(w);
        double alpha = Math.Sin(w) / 2 * Math.Sqrt((A + 1 / A) * (1 / s - 1) + 2);
        double sq = 2 * Math.Sqrt(A) * alpha;
        Set(A * ((A + 1) - (A - 1) * c + sq), 2 * A * ((A - 1) - (A + 1) * c), A * ((A + 1) - (A - 1) * c - sq),
            (A + 1) + (A - 1) * c + sq, -2 * ((A - 1) + (A + 1) * c), (A + 1) + (A - 1) * c - sq);
    }

    public void HighShelf(double fs, double f, double gainDb, double s = 0.8)
    {
        double A = Math.Pow(10, gainDb / 40);
        double w = 2 * Math.PI * Math.Clamp(f, 1, fs * 0.49) / fs, c = Math.Cos(w);
        double alpha = Math.Sin(w) / 2 * Math.Sqrt((A + 1 / A) * (1 / s - 1) + 2);
        double sq = 2 * Math.Sqrt(A) * alpha;
        Set(A * ((A + 1) + (A - 1) * c + sq), -2 * A * ((A - 1) + (A + 1) * c), A * ((A + 1) + (A - 1) * c - sq),
            (A + 1) - (A - 1) * c + sq, 2 * ((A - 1) - (A + 1) * c), (A + 1) - (A - 1) * c - sq);
    }
}
