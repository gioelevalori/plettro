using System.IO;
using System.Text.Json;

namespace Plettro;

public sealed class AppSettings
{
    public string? InputDeviceId { get; set; }
    public string? OutputDeviceId { get; set; }
    public int ChannelMode { get; set; }
    public double A4 { get; set; } = 440;
    public int TuningIndex { get; set; }
    public bool ItalianNames { get; set; } = true;
    public double GateDb { get; set; } = -55;
    public double ToneVolume { get; set; } = 0.5;

    public double Bpm { get; set; } = 100;
    public int BeatsPerBar { get; set; } = 4;
    public int Subdivision { get; set; } = 1;
    public int Sound { get; set; }
    public double Volume { get; set; } = 0.8;
    public int[]? Accents { get; set; }

    public bool TrainerEnabled { get; set; }
    public double TrainerTarget { get; set; } = 140;
    public double TrainerStep { get; set; } = 5;
    public int TrainerEvery { get; set; } = 4;

    public bool GapEnabled { get; set; }
    public int GapPlay { get; set; } = 3;
    public int GapMute { get; set; } = 1;

    static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Plettro", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch { /* corrupted settings: start fresh */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
