namespace Plettro.Music;

public static class NoteUtil
{
    static readonly string[] English = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
    static readonly string[] Italian = { "Do", "Do#", "Re", "Re#", "Mi", "Fa", "Fa#", "Sol", "Sol#", "La", "La#", "Si" };

    public static double MidiToFreq(double midi, double a4) => a4 * Math.Pow(2, (midi - 69) / 12.0);

    public static double FreqToMidi(double freq, double a4) => 69 + 12 * Math.Log2(freq / a4);

    public static string EnglishName(int midi) => English[Mod12(midi)];

    public static string ItalianName(int midi) => Italian[Mod12(midi)];

    public static int Octave(int midi) => (int)Math.Floor(midi / 12.0) - 1;

    static int Mod12(int v) => ((v % 12) + 12) % 12;
}

public sealed record Tuning(string Name, int[] Notes)
{
    public override string ToString() => Name;
}

public static class Tunings
{
    // MIDI notes, from the lowest (thickest) string to the highest.
    public static readonly Tuning[] All =
    {
        new("Standard (E A D G B E)",          new[] { 40, 45, 50, 55, 59, 64 }),
        new("Drop D (D A D G B E)",            new[] { 38, 45, 50, 55, 59, 64 }),
        new("Mezzo tono sotto (Eb)",           new[] { 39, 44, 49, 54, 58, 63 }),
        new("Un tono sotto (D standard)",      new[] { 38, 43, 48, 53, 57, 62 }),
        new("Drop C# (C# G# C# F# A# D#)",     new[] { 37, 44, 49, 54, 58, 63 }),
        new("Drop C (C G C F A D)",            new[] { 36, 43, 48, 53, 57, 62 }),
        new("Drop B (B F# B E G# C#)",         new[] { 35, 42, 47, 52, 56, 61 }),
        new("C standard (C F A# D# G C)",      new[] { 36, 41, 46, 51, 55, 60 }),
        new("Open G (D G D G B D)",            new[] { 38, 43, 50, 55, 59, 62 }),
        new("Open D (D A D F# A D)",           new[] { 38, 45, 50, 54, 57, 62 }),
        new("Open E (E B E G# B E)",           new[] { 40, 47, 52, 56, 59, 64 }),
        new("DADGAD",                          new[] { 38, 45, 50, 55, 57, 62 }),
        new("7 corde standard (B E A D G B E)",new[] { 35, 40, 45, 50, 55, 59, 64 }),
        new("7 corde Drop A (A E A D G B E)",  new[] { 33, 40, 45, 50, 55, 59, 64 }),
        new("Basso 4 corde (E A D G)",         new[] { 28, 33, 38, 43 }),
        new("Basso 5 corde (B E A D G)",       new[] { 23, 28, 33, 38, 43 }),
    };
}
