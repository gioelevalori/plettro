namespace Plettro.Audio.Fx;

/// <summary>Serializable snapshot of every GuitarRig parameter (used for presets and saving).</summary>
public sealed class RigSettings
{
    public bool GateOn { get; set; } = true;
    public double GateThresholdDb { get; set; } = -60;

    public bool DriveOn { get; set; }
    public double Drive { get; set; } = 4;
    public double DriveTone { get; set; } = 5;
    public double DriveLevel { get; set; } = 6;

    public AmpModel Model { get; set; } = AmpModel.Lead;
    public double Gain { get; set; } = 5.5;
    public double Bass { get; set; } = 6;
    public double Mid { get; set; } = 6.5;
    public double Treble { get; set; } = 6.5;
    public double Presence { get; set; } = 6;
    public double Master { get; set; } = 6;

    public CabType Cab { get; set; } = CabType.Cab4x12;
    public string? IrPath { get; set; }

    public bool DelayOn { get; set; }
    public double DelayMs { get; set; } = 380;
    public double DelayFeedback { get; set; } = 3;
    public double DelayMix { get; set; } = 3;

    public bool ReverbOn { get; set; } = true;
    public double ReverbSize { get; set; } = 3;
    public double ReverbMix { get; set; } = 0.8;

    public double OutputDb { get; set; } = 0;

    public RigSettings Clone() => (RigSettings)MemberwiseClone();

    public void ApplyTo(GuitarRig r)
    {
        r.GateOn = GateOn; r.GateThresholdDb = GateThresholdDb;
        r.DriveOn = DriveOn; r.Drive = Drive; r.DriveTone = DriveTone; r.DriveLevel = DriveLevel;
        r.Model = Model; r.Gain = Gain; r.Bass = Bass; r.Mid = Mid; r.Treble = Treble; r.Presence = Presence; r.Master = Master;
        // IR cab without a loaded IR falls back to the simulated 4x12
        r.Cab = Cab == CabType.ImpulseResponse && r.ImpulseResponse == null ? CabType.Cab4x12 : Cab;
        r.DelayOn = DelayOn; r.DelayMs = DelayMs; r.DelayFeedback = DelayFeedback; r.DelayMix = DelayMix;
        r.ReverbOn = ReverbOn; r.ReverbSize = ReverbSize; r.ReverbMix = ReverbMix;
        r.OutputDb = OutputDb;
    }
}

public sealed record RigPreset(string Name, string Description, RigSettings Settings);

public static class RigPresets
{
    public static readonly RigPreset[] All =
    {
        new("Punk rock (stile Green Day)",
            "Ampli britannico molto spinto, medi in avanti, attacco stretto per le pennate stoppate, cassa 4x12 e riverbero quasi assente.",
            new RigSettings
            {
                GateOn = true, GateThresholdDb = -58,
                Model = AmpModel.Lead, Gain = 5.5, Bass = 5.5, Mid = 6.5, Treble = 7, Presence = 6.5, Master = 6,
                Cab = CabType.Cab4x12, ReverbOn = true, ReverbSize = 3, ReverbMix = 0.8,
            }),
        new("Pulito cristallino",
            "Suono pulito e brillante, un po' di riverbero. Per arpeggi e accordi aperti.",
            new RigSettings
            {
                GateOn = true, GateThresholdDb = -66,
                Model = AmpModel.Clean, Gain = 3, Bass = 5, Mid = 5, Treble = 6.5, Presence = 6, Master = 5,
                Cab = CabType.Cab2x12, ReverbOn = true, ReverbSize = 6, ReverbMix = 2.5,
            }),
        new("Crunch blues",
            "Leggera saturazione che si pulisce abbassando il volume della chitarra.",
            new RigSettings
            {
                GateOn = false,
                Model = AmpModel.Crunch, Gain = 5, Bass = 5.5, Mid = 6, Treble = 5.5, Presence = 5, Master = 5.5,
                Cab = CabType.Cab2x12, ReverbOn = true, ReverbSize = 5, ReverbMix = 1.8,
            }),
        new("Rock classico",
            "Crunch spinto da un overdrive usato come booster. Per riff hard rock anni '70-'80.",
            new RigSettings
            {
                GateOn = true, GateThresholdDb = -62,
                DriveOn = true, Drive = 3, DriveTone = 5, DriveLevel = 7,
                Model = AmpModel.Crunch, Gain = 7.5, Bass = 5.5, Mid = 6.5, Treble = 6, Presence = 5.5, Master = 5.5,
                Cab = CabType.Cab4x12, ReverbOn = true, ReverbSize = 4, ReverbMix = 1.2,
            }),
        new("Assolo lead",
            "Molto sustain, medi pieni, delay e riverbero per gli assoli.",
            new RigSettings
            {
                GateOn = true, GateThresholdDb = -60,
                Model = AmpModel.Lead, Gain = 7, Bass = 5.5, Mid = 7, Treble = 6, Presence = 5.5, Master = 5.5,
                Cab = CabType.Cab4x12,
                DelayOn = true, DelayMs = 400, DelayFeedback = 3, DelayMix = 2.5,
                ReverbOn = true, ReverbSize = 6, ReverbMix = 2,
            }),
        new("Metal",
            "Tantissimo gain, gate deciso e overdrive come booster per un attacco stretto.",
            new RigSettings
            {
                GateOn = true, GateThresholdDb = -50,
                DriveOn = true, Drive = 1, DriveTone = 6, DriveLevel = 8,
                Model = AmpModel.Metal, Gain = 7, Bass = 6, Mid = 4, Treble = 6.5, Presence = 6.5, Master = 5.5,
                Cab = CabType.Cab4x12, ReverbOn = true, ReverbSize = 3, ReverbMix = 0.8,
            }),
    };
}
