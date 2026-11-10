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
        new("Pop punk",
            "Distorto, compatto e brillante, con un overdrive come booster per pennate veloci e precise. Stile pop punk anni 2000.",
            new RigSettings
            {
                GateOn = true, GateThresholdDb = -56,
                DriveOn = true, Drive = 1.5, DriveTone = 6, DriveLevel = 7,
                Model = AmpModel.Lead, Gain = 5.5, Bass = 6, Mid = 5.5, Treble = 7, Presence = 6.5, Master = 6,
                Cab = CabType.Cab4x12, ReverbOn = true, ReverbSize = 3, ReverbMix = 0.6,
            }),
        new("Grunge anni '90",
            "Distorsione grossa e un po' sporca, medi leggermente scavati e tanti bassi. Per riff pesanti e strofe pulite-forti.",
            new RigSettings
            {
                GateOn = true, GateThresholdDb = -58,
                DriveOn = true, Drive = 7, DriveTone = 4.5, DriveLevel = 6,
                Model = AmpModel.Crunch, Gain = 6.5, Bass = 6.5, Mid = 4.5, Treble = 6, Presence = 5.5, Master = 6,
                Cab = CabType.Cab4x12, ReverbOn = true, ReverbSize = 4, ReverbMix = 1,
            }),
        new("Hard rock anni '80",
            "Lead britannico con medi in avanti e un po' di riverbero: riff e assoli hard rock / glam.",
            new RigSettings
            {
                GateOn = true, GateThresholdDb = -58,
                DriveOn = true, Drive = 2, DriveTone = 6, DriveLevel = 7,
                Model = AmpModel.Lead, Gain = 6.5, Bass = 5.5, Mid = 7, Treble = 6.5, Presence = 6, Master = 6,
                Cab = CabType.Cab4x12, DelayOn = true, DelayMs = 330, DelayFeedback = 2, DelayMix = 1.5,
                ReverbOn = true, ReverbSize = 5, ReverbMix = 1.8,
            }),
        new("Thrash metal",
            "Molto gain, medi scavati, attacco stretto e gate deciso per le ritmiche in pennata alternata.",
            new RigSettings
            {
                GateOn = true, GateThresholdDb = -48,
                DriveOn = true, Drive = 0.5, DriveTone = 7, DriveLevel = 8,
                Model = AmpModel.Metal, Gain = 6, Bass = 6.5, Mid = 3.5, Treble = 7, Presence = 7, Master = 5.5,
                Cab = CabType.Cab4x12, ReverbOn = false,
            }),
        new("Indie rock",
            "Crunch medio, brillante e articolato, cassa aperta 2x12: si sentono tutte le note degli accordi.",
            new RigSettings
            {
                GateOn = true, GateThresholdDb = -62,
                Model = AmpModel.Crunch, Gain = 6, Bass = 5, Mid = 6.5, Treble = 6.5, Presence = 6, Master = 6,
                Cab = CabType.Cab2x12, ReverbOn = true, ReverbSize = 5, ReverbMix = 1.5,
            }),
        new("Blues elettrico",
            "Ampli appena saturo spinto da un overdrive caldo: risponde al tocco e alla manopola del volume.",
            new RigSettings
            {
                GateOn = false,
                DriveOn = true, Drive = 4, DriveTone = 5, DriveLevel = 6,
                Model = AmpModel.Crunch, Gain = 4, Bass = 5.5, Mid = 6.5, Treble = 5.5, Presence = 5, Master = 6,
                Cab = CabType.Cab2x12, ReverbOn = true, ReverbSize = 6, ReverbMix = 2.5,
            }),
        new("Funk pulito",
            "Pulito secco e brillante, pochi bassi, cassa piccola: per ritmiche funk e accordi stoppati.",
            new RigSettings
            {
                GateOn = true, GateThresholdDb = -64,
                Model = AmpModel.Clean, Gain = 2.5, Bass = 4, Mid = 4.5, Treble = 7.5, Presence = 7, Master = 5,
                Cab = CabType.Cab1x12, ReverbOn = true, ReverbSize = 3, ReverbMix = 0.8,
            }),
        new("Surf rock",
            "Pulito brillante con tanto riverbero, per il classico suono surf anni '60.",
            new RigSettings
            {
                GateOn = true, GateThresholdDb = -64,
                Model = AmpModel.Clean, Gain = 3.5, Bass = 4.5, Mid = 5, Treble = 7.5, Presence = 6.5, Master = 5.5,
                Cab = CabType.Cab1x12, ReverbOn = true, ReverbSize = 8, ReverbMix = 5,
            }),
        new("Rockabilly slapback",
            "Leggero crunch con un'eco corta e secca (slapback), come nel rock'n'roll anni '50.",
            new RigSettings
            {
                GateOn = false,
                Model = AmpModel.Crunch, Gain = 3.5, Bass = 5, Mid = 6, Treble = 6.5, Presence = 6, Master = 5.5,
                Cab = CabType.Cab1x12, DelayOn = true, DelayMs = 110, DelayFeedback = 0.5, DelayMix = 4,
                ReverbOn = true, ReverbSize = 3, ReverbMix = 0.8,
            }),
        new("Ambient",
            "Pulito con delay lungo e riverbero enorme: suoni atmosferici, arpeggi lenti e note lunghe.",
            new RigSettings
            {
                GateOn = true, GateThresholdDb = -66,
                Model = AmpModel.Clean, Gain = 3.5, Bass = 5, Mid = 5, Treble = 6, Presence = 5.5, Master = 5,
                Cab = CabType.Cab2x12, DelayOn = true, DelayMs = 520, DelayFeedback = 5.5, DelayMix = 4,
                ReverbOn = true, ReverbSize = 9.5, ReverbMix = 5.5,
            }),
    };
}
