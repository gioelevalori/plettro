using System.IO;
using System.Text.Json;

namespace Plettro;

public sealed class AppSettings
{
    public string? InputDeviceId { get; set; }
    public string? OutputDeviceId { get; set; }
    /// <summary>True once the user picked a device by hand; until then the Scarlett is preferred.</summary>
    public bool DevicesChosenByUser { get; set; }
    public int ChannelMode { get; set; }
    public double A4 { get; set; } = 440;
    public int TuningIndex { get; set; }
    public bool ItalianNames { get; set; } = true;
    public double GateDb { get; set; } = -55;
    public double ToneVolume { get; set; } = 0.5;

    public double Bpm { get; set; } = 100;
    public int BeatsPerBar { get; set; } = 4;
    public int Subdivision { get; set; } = 1;
    public int Sound { get; set; } = 5; // drum kit
    public int SettingsVersion { get; set; }
    public double Volume { get; set; } = 0.8;
    public int[]? Accents { get; set; }

    public bool TrainerEnabled { get; set; }
    public double TrainerTarget { get; set; } = 140;
    public double TrainerStep { get; set; } = 5;
    public int TrainerEvery { get; set; } = 4;

    public Plettro.Audio.Fx.RigSettings? Rig { get; set; }
    public string? FxDriver { get; set; }
    public int FxInputChannel { get; set; } = 1;
    public double? FxNoiseFloorDb { get; set; }

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
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
                if (s.SettingsVersion < 1) { s.Sound = 5; s.SettingsVersion = 1; } // v1.1: drum kit is the new default
                return s;
            }
        }
        catch { /* corrupted settings: start fresh */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            SettingsVersion = 1;
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
