using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using NAudio.CoreAudioApi;
using Plettro.Audio;
using Plettro.Music;
using ShapePath = System.Windows.Shapes.Path;

namespace Plettro;

public partial class MainWindow : Window
{
    sealed record DeviceItem(string Id, string Name)
    {
        public override string ToString() => Name;
    }

    sealed class StringButton
    {
        public required Button Button;
        public required TextBlock Note;
        public required TextBlock Label;
        public bool Tuned;
    }

    readonly AppSettings settings = AppSettings.Load();
    readonly InputMonitor input = new();
    readonly AudioOutput output = new();
    readonly MMDeviceEnumerator enumerator = new();

    static readonly Brush Accent = Frozen(0xFF, 0x8A, 0x3D);
    static readonly Brush Good = Frozen(0x3D, 0xDC, 0x84);
    static readonly Brush Warn = Frozen(0xFF, 0xC5, 0x3D);
    static readonly Brush Bad = Frozen(0xFF, 0x5A, 0x5A);
    static readonly Brush Muted = Frozen(0x8A, 0x8F, 0x98);
    static readonly Brush Faint = Frozen(0x3A, 0x3D, 0x44);
    static readonly Brush TextBrush = Frozen(0xF0, 0xF0, 0xF0);
    static readonly Brush ClipOff = FrozenA(0x33, 0xFF, 0x5A, 0x5A);

    static Brush FrozenA(byte a, byte r, byte g, byte b)
    {
        var br = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        br.Freeze();
        return br;
    }

    bool loaded;
    bool suppressDeviceEvents;

    // tuner
    Line needle = null!;
    readonly RotateTransform needleRotate = new();
    readonly List<StringButton> stringButtons = new();
    int lockedString = -1;
    double displayCents;
    bool hadSignal;
    double inTuneSeconds;
    int lastTarget = -1;
    double levelDisplay = -90;
    TimeSpan lastFrame;

    // metronome
    readonly List<(Border Dot, ScaleTransform Scale)> beatDots = new();
    long lastEventId = -1;
    readonly List<double> tapTimes = new();
    readonly Stopwatch tapWatch = Stopwatch.StartNew();
    bool suppressBpm;

    // session
    readonly Stopwatch session = new();

    static Brush Frozen(byte r, byte g, byte b)
    {
        var br = new SolidColorBrush(Color.FromRgb(r, g, b));
        br.Freeze();
        return br;
    }

    Tuning CurrentTuning => Tunings.All[Math.Clamp(TuningCombo.SelectedIndex, 0, Tunings.All.Length - 1)];
    MetronomeProvider Metro => output.Metronome;

    public MainWindow()
    {
        InitializeComponent();
    }

    // =====================================================================
    // Startup / shutdown
    // =====================================================================

    void Window_Loaded(object sender, RoutedEventArgs e)
    {
        BuildGauge();

        foreach (var t in Tunings.All) TuningCombo.Items.Add(t);
        TuningCombo.SelectedIndex = Math.Clamp(settings.TuningIndex, 0, Tunings.All.Length - 1);

        for (int i = 1; i <= MetronomeProvider.MaxBeats; i++) BeatsCombo.Items.Add($"{i}/4");

        // apply saved settings
        ItalianCheck.IsChecked = settings.ItalianNames;
        GateSlider.Value = settings.GateDb;
        ToneVolSlider.Value = settings.ToneVolume * 100;
        ChannelCombo.SelectedIndex = Math.Clamp(settings.ChannelMode, 0, 3);
        input.Mode = (ChannelMode)ChannelCombo.SelectedIndex;
        input.GateDb = settings.GateDb;
        output.Tone.Volume = (float)settings.ToneVolume;

        if (settings.Accents is { Length: MetronomeProvider.MaxBeats })
            for (int i = 0; i < MetronomeProvider.MaxBeats; i++) Metro.Accents[i] = (AccentLevel)Math.Clamp(settings.Accents[i], 0, 2);

        Metro.Bpm = settings.Bpm;
        Metro.BeatsPerBar = Math.Clamp(settings.BeatsPerBar, 1, MetronomeProvider.MaxBeats);
        Metro.Subdivision = settings.Subdivision;
        Metro.Sound = (ClickSound)Math.Clamp(settings.Sound, 0, 5);
        Metro.Volume = (float)settings.Volume;

        BpmSlider.Value = Metro.Bpm;
        BeatsCombo.SelectedIndex = Metro.BeatsPerBar - 1;
        SubdivCombo.SelectedIndex = Metro.Subdivision switch { 2 => 1, 3 => 2, 4 => 3, 6 => 4, _ => 0 };
        SoundCombo.SelectedIndex = (int)Metro.Sound;
        VolumeSlider.Value = settings.Volume * 100;

        TrainerCheck.IsChecked = settings.TrainerEnabled;
        TrainerTargetSlider.Value = settings.TrainerTarget;
        TrainerStepSlider.Value = settings.TrainerStep;
        TrainerEverySlider.Value = settings.TrainerEvery;
        GapCheck.IsChecked = settings.GapEnabled;
        GapPlaySlider.Value = settings.GapPlay;
        GapMuteSlider.Value = settings.GapMute;

        loaded = true;

        UpdateA4Text();
        RebuildStrings();
        RebuildBeatDots();
        UpdateBpmText();
        ApplyTrainer();
        ApplyGap();
        VolumeText.Text = $"{VolumeSlider.Value:0}%";
        GateText.Text = $"{GateSlider.Value:0} dB";
        ToneVolText.Text = $"{ToneVolSlider.Value:0}%";

        RefreshDevices();
        BuildFxTab();

        CompositionTarget.Rendering += OnRendering;
    }

    void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        CompositionTarget.Rendering -= OnRendering;
        SaveSettings();
        live.Dispose();
        input.Dispose();
        output.Dispose();
    }

    void SaveSettings()
    {
        settings.TuningIndex = TuningCombo.SelectedIndex;
        settings.ChannelMode = ChannelCombo.SelectedIndex;
        settings.ItalianNames = ItalianCheck.IsChecked == true;
        settings.GateDb = GateSlider.Value;
        settings.ToneVolume = ToneVolSlider.Value / 100;
        settings.Bpm = Metro.Bpm;
        settings.BeatsPerBar = Metro.BeatsPerBar;
        settings.Subdivision = Metro.Subdivision;
        settings.Sound = (int)Metro.Sound;
        settings.Volume = VolumeSlider.Value / 100;
        settings.Accents = Metro.Accents.Select(a => (int)a).ToArray();
        settings.TrainerEnabled = TrainerCheck.IsChecked == true;
        settings.TrainerTarget = TrainerTargetSlider.Value;
        settings.TrainerStep = TrainerStepSlider.Value;
        settings.TrainerEvery = (int)TrainerEverySlider.Value;
        settings.GapEnabled = GapCheck.IsChecked == true;
        settings.GapPlay = (int)GapPlaySlider.Value;
        settings.GapMute = (int)GapMuteSlider.Value;
        SaveFxSettings();
        settings.Save();
    }

    // =====================================================================
    // Devices
    // =====================================================================

    static bool IsFocusrite(string name) =>
        name.Contains("Focusrite", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Scarlett", StringComparison.OrdinalIgnoreCase);

    void RefreshDevices()
    {
        suppressDeviceEvents = true;
        try
        {
            InputCombo.Items.Clear();
            OutputCombo.Items.Clear();
            var inputs = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                .Select(d => new DeviceItem(d.ID, d.FriendlyName)).ToList();
            var outputs = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                .Select(d => new DeviceItem(d.ID, d.FriendlyName)).ToList();
            foreach (var d in inputs) InputCombo.Items.Add(d);
            foreach (var d in outputs) OutputCombo.Items.Add(d);

            InputCombo.SelectedItem = PickDevice(inputs, settings.InputDeviceId, DataFlow.Capture);
            OutputCombo.SelectedItem = PickDevice(outputs, settings.OutputDeviceId, DataFlow.Render);
        }
        finally { suppressDeviceEvents = false; }

        StartInput();
        StartOutput();
    }

    DeviceItem? PickDevice(List<DeviceItem> list, string? savedId, DataFlow flow)
    {
        if (list.Count == 0) return null;
        var saved = list.FirstOrDefault(d => d.Id == savedId);
        var focusrite = list.FirstOrDefault(d => IsFocusrite(d.Name));
        // a device picked by hand always wins; an automatic pick yields to the Scarlett when it is plugged in
        if (saved != null && (settings.DevicesChosenByUser || focusrite == null)) return saved;
        if (focusrite != null) return focusrite;
        if (saved != null) return saved;
        try
        {
            if (enumerator.HasDefaultAudioEndpoint(flow, Role.Multimedia))
            {
                var def = enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia);
                var match = list.FirstOrDefault(d => d.Id == def.ID);
                if (match != null) return match;
            }
        }
        catch { }
        return list[0];
    }

    void StartInput()
    {
        input.Stop();
        if (InputCombo.SelectedItem is not DeviceItem item)
        {
            InputInfoText.Text = "Nessun dispositivo di ingresso trovato. Collega la Scarlett e premi \"Aggiorna\" nelle impostazioni.";
            UpdateDeviceStatus();
            return;
        }
        try
        {
            var dev = enumerator.GetDevice(item.Id);
            input.Start(dev);
            settings.InputDeviceId = item.Id;
            StatusText.Text = "";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Impossibile aprire l'ingresso \"{item.Name}\": {ex.Message}";
        }
        UpdateDeviceStatus();
    }

    void StartOutput()
    {
        bool wasRunning = Metro.IsRunning;
        output.Stop();
        if (OutputCombo.SelectedItem is not DeviceItem item) { UpdateDeviceStatus(); return; }
        try
        {
            var dev = enumerator.GetDevice(item.Id);
            output.Start(dev);
            settings.OutputDeviceId = item.Id;
            if (wasRunning) Metro.Start();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Impossibile aprire l'uscita \"{item.Name}\": {ex.Message}";
        }
        UpdateStartButton();
        UpdateDeviceStatus();
    }

    void UpdateDeviceStatus()
    {
        var parts = new List<string>();
        if (input.IsRunning) parts.Add($"Ingresso attivo: {input.SampleRate / 1000.0:0.#} kHz, {input.Channels} canali.");
        else parts.Add("Ingresso non attivo.");
        parts.Add(output.DeviceName.Length > 0 ? $"Uscita attiva: {output.DeviceName} ({output.SampleRate / 1000.0:0.#} kHz)." : "Uscita non attiva.");
        if (InputCombo.Items.Cast<DeviceItem>().Any(d => IsFocusrite(d.Name)))
            parts.Add("✓ Scarlett / Focusrite rilevata.");
        else
            parts.Add("Scarlett non rilevata: verifica il cavo USB e i driver Focusrite.");
        DeviceStatusText.Text = string.Join("\n", parts);
    }

    void InputCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!loaded || suppressDeviceEvents) return;
        settings.DevicesChosenByUser = true;
        StartInput();
    }

    void OutputCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!loaded || suppressDeviceEvents) return;
        settings.DevicesChosenByUser = true;
        StartOutput();
    }

    void ChannelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!loaded) return;
        input.Mode = (ChannelMode)Math.Max(0, ChannelCombo.SelectedIndex);
    }

    void RefreshDevices_Click(object sender, RoutedEventArgs e) => RefreshDevices();

    void GateSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!loaded) return;
        input.GateDb = GateSlider.Value;
        GateText.Text = $"{GateSlider.Value:0} dB";
    }

    void ToneVolSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!loaded) return;
        output.Tone.Volume = (float)(ToneVolSlider.Value / 100);
        ToneVolText.Text = $"{ToneVolSlider.Value:0}%";
    }

    void ItalianCheck_Click(object sender, RoutedEventArgs e) => RebuildStrings();

    // =====================================================================
    // Render loop
    // =====================================================================

    void OnRendering(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        double dt = lastFrame == TimeSpan.Zero ? 0.016 : Math.Clamp((now - lastFrame).TotalSeconds, 0, 0.1);
        if (now == lastFrame) return;
        lastFrame = now;

        if (Tabs.SelectedIndex == 0)
        {
            UpdateTuner(dt);
            UpdateLevel(dt);
        }
        UpdateMetronome(dt, Tabs.SelectedIndex == 1);
        CheckCalibration();
        if (Tabs.SelectedIndex == 2) UpdateFxMeters(dt);
        UpdateSession();
    }

    // =====================================================================
    // Tuner
    // =====================================================================

    const double GaugeCx = 250, GaugeCy = 272, GaugeR = 220;

    static double CentsToAngle(double cents) => Math.Clamp(cents, -50, 50) / 50.0 * 60.0;

    Point PolarPoint(double angleDeg, double radius)
    {
        double a = angleDeg * Math.PI / 180;
        return new Point(GaugeCx + Math.Sin(a) * radius, GaugeCy - Math.Cos(a) * radius);
    }

    ShapePath ArcPath(double fromDeg, double toDeg, double radius, Brush stroke, double thickness)
    {
        var start = PolarPoint(fromDeg, radius);
        var end = PolarPoint(toDeg, radius);
        var fig = new PathFigure { StartPoint = start, IsClosed = false };
        fig.Segments.Add(new ArcSegment(end, new Size(radius, radius), 0, Math.Abs(toDeg - fromDeg) > 180,
            SweepDirection.Clockwise, true));
        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        return new ShapePath { Data = geo, Stroke = stroke, StrokeThickness = thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
    }

    void BuildGauge()
    {
        Gauge.Children.Clear();
        Gauge.Children.Add(ArcPath(-60, 60, GaugeR + 2, Faint, 2));
        var greenZone = ArcPath(CentsToAngle(-5), CentsToAngle(5), GaugeR - 10, Good, 16);
        greenZone.Opacity = 0.35;
        Gauge.Children.Add(greenZone);

        for (int c = -50; c <= 50; c += 5)
        {
            double angle = CentsToAngle(c);
            double len = c % 25 == 0 ? 24 : (c % 10 == 0 ? 15 : 9);
            var p1 = PolarPoint(angle, GaugeR);
            var p2 = PolarPoint(angle, GaugeR - len);
            Gauge.Children.Add(new Line
            {
                X1 = p1.X, Y1 = p1.Y, X2 = p2.X, Y2 = p2.Y,
                Stroke = c == 0 ? Good : Muted,
                StrokeThickness = c % 25 == 0 ? 3 : 1.5,
            });
            if (c % 25 == 0)
            {
                var lp = PolarPoint(angle, GaugeR + 20);
                var tb = new TextBlock
                {
                    Text = c > 0 ? $"+{c}" : c.ToString(),
                    Foreground = Muted, FontSize = 14, Width = 40, TextAlignment = TextAlignment.Center,
                };
                Canvas.SetLeft(tb, lp.X - 20);
                Canvas.SetTop(tb, lp.Y - 10);
                Gauge.Children.Add(tb);
            }
        }

        var flat = new TextBlock { Text = "♭", FontSize = 34, Foreground = Muted };
        Canvas.SetLeft(flat, 40); Canvas.SetTop(flat, 205);
        var sharp = new TextBlock { Text = "♯", FontSize = 34, Foreground = Muted };
        Canvas.SetLeft(sharp, 440); Canvas.SetTop(sharp, 205);
        Gauge.Children.Add(flat);
        Gauge.Children.Add(sharp);

        needleRotate.CenterX = GaugeCx;
        needleRotate.CenterY = GaugeCy;
        needle = new Line
        {
            X1 = GaugeCx, Y1 = GaugeCy, X2 = GaugeCx, Y2 = GaugeCy - GaugeR + 14,
            Stroke = Muted, StrokeThickness = 5,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            RenderTransform = needleRotate,
        };
        Gauge.Children.Add(needle);
        var pivot = new Ellipse { Width = 22, Height = 22, Fill = TextBrush };
        Canvas.SetLeft(pivot, GaugeCx - 11);
        Canvas.SetTop(pivot, GaugeCy - 11);
        Gauge.Children.Add(pivot);
    }

    void RebuildStrings()
    {
        if (!loaded) return;
        StringsPanel.Children.Clear();
        stringButtons.Clear();
        var tuning = CurrentTuning;
        bool it = ItalianCheck.IsChecked == true;
        int n = tuning.Notes.Length;
        for (int i = 0; i < n; i++)
        {
            int midi = tuning.Notes[i];
            int stringNumber = n - i; // lowest string has the highest number
            var note = new TextBlock
            {
                Text = NoteUtil.EnglishName(midi) + NoteUtil.Octave(midi),
                FontSize = 20, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center,
            };
            var label = new TextBlock
            {
                Text = it ? $"{stringNumber}ª · {NoteUtil.ItalianName(midi)}" : $"{stringNumber}ª corda",
                FontSize = 12, Foreground = Muted, HorizontalAlignment = HorizontalAlignment.Center,
            };
            var sp = new StackPanel();
            sp.Children.Add(note);
            sp.Children.Add(label);
            var btn = new Button
            {
                Content = sp, Width = 94, Height = 64, Margin = new Thickness(4, 0, 4, 0),
                Focusable = false, Tag = i, BorderThickness = new Thickness(2),
                ToolTip = "Clic: blocca l'accordatore su questa corda e riproduce la nota",
            };
            btn.Click += StringButton_Click;
            StringsPanel.Children.Add(btn);
            stringButtons.Add(new StringButton { Button = btn, Note = note, Label = label });
        }
        if (lockedString >= n) lockedString = -1;
        UpdateStringVisuals(-1);
        UpdateModeButtons();
    }

    void StringButton_Click(object sender, RoutedEventArgs e)
    {
        int idx = (int)((Button)sender).Tag;
        lockedString = lockedString == idx ? -1 : idx;
        if (lockedString >= 0) PlayReference(CurrentTuning.Notes[lockedString]);
        UpdateModeButtons();
    }

    void AutoMode_Click(object sender, RoutedEventArgs e)
    {
        lockedString = -1;
        UpdateModeButtons();
    }

    void UpdateModeButtons()
    {
        AutoModeButton.FontWeight = lockedString < 0 ? FontWeights.Bold : FontWeights.Normal;
        AutoModeButton.BorderBrush = lockedString < 0 ? Accent : null;
        AutoModeButton.BorderThickness = new Thickness(lockedString < 0 ? 2 : 1);
    }

    void RefTone_Click(object sender, RoutedEventArgs e)
    {
        var t = CurrentTuning;
        int midi = lockedString >= 0 ? t.Notes[lockedString] : (lastTarget > 0 ? lastTarget : t.Notes[0]);
        PlayReference(midi);
    }

    void PlayReference(int midi)
    {
        output.Tone.Play(NoteUtil.MidiToFreq(midi, settings.A4), 3.0);
        StatusText.Text = $"Nota di riferimento: {NoteUtil.EnglishName(midi)}{NoteUtil.Octave(midi)} ({NoteUtil.MidiToFreq(midi, settings.A4):0.00} Hz)";
    }

    void TuningCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!loaded) return;
        lockedString = -1;
        RebuildStrings();
    }

    void A4Down_Click(object sender, RoutedEventArgs e) { settings.A4 = Math.Max(415, settings.A4 - 1); UpdateA4Text(); }
    void A4Up_Click(object sender, RoutedEventArgs e) { settings.A4 = Math.Min(466, settings.A4 + 1); UpdateA4Text(); }
    void UpdateA4Text() => A4Text.Text = $"{settings.A4:0} Hz";

    void UpdateTuner(double dt)
    {
        var r = input.Latest;
        bool active = r.Frequency > 0 && (DateTime.UtcNow - r.TimeUtc).TotalMilliseconds < 350;
        var tuning = CurrentTuning;

        if (!active)
        {
            hadSignal = false;
            inTuneSeconds = 0;
            displayCents += (0 - displayCents) * Math.Min(1, dt * 4);
            if (Math.Abs(displayCents) < 0.05) displayCents = 0;
            needleRotate.Angle = CentsToAngle(displayCents);
            needle.Stroke = Faint;
            NoteText.Foreground = Muted;
            OctaveText.Foreground = Muted;
            if (lockedString >= 0)
            {
                int m = tuning.Notes[lockedString];
                NoteText.Text = NoteUtil.EnglishName(m);
                OctaveText.Text = NoteUtil.Octave(m).ToString();
                NoteItText.Text = $"Corda bloccata: {NoteUtil.MidiToFreq(m, settings.A4):0.00} Hz";
            }
            else
            {
                NoteText.Text = "–";
                OctaveText.Text = "";
                NoteItText.Text = "Suona una corda";
            }
            FreqText.Text = "— Hz";
            CentsText.Text = "— cent";
            HintText.Text = " ";
            UpdateStringVisuals(-1);
            return;
        }

        double midiF = NoteUtil.FreqToMidi(r.Frequency, settings.A4);
        int target = lockedString >= 0 ? tuning.Notes[lockedString] : (int)Math.Round(midiF);
        double cents = (midiF - target) * 100;
        if (target != lastTarget || !hadSignal) displayCents = cents;
        else displayCents += (cents - displayCents) * Math.Min(1, dt * 14);
        hadSignal = true;
        lastTarget = target;

        double abs = Math.Abs(cents);
        Brush color = abs <= 3 ? Good : abs <= 12 ? Warn : Bad;

        needleRotate.Angle = CentsToAngle(displayCents);
        needle.Stroke = color;
        NoteText.Text = NoteUtil.EnglishName(target);
        OctaveText.Text = NoteUtil.Octave(target).ToString();
        NoteText.Foreground = color;
        OctaveText.Foreground = color;
        NoteItText.Text = ItalianCheck.IsChecked == true
            ? $"{NoteUtil.ItalianName(target)} · obiettivo {NoteUtil.MidiToFreq(target, settings.A4):0.00} Hz"
            : $"obiettivo {NoteUtil.MidiToFreq(target, settings.A4):0.00} Hz";
        FreqText.Text = $"{r.Frequency:0.00} Hz";
        CentsText.Text = $"{(cents >= 0 ? "+" : "")}{cents:0} cent";

        int matched = -1;
        if (lockedString >= 0) matched = lockedString;
        else
        {
            for (int i = 0; i < tuning.Notes.Length; i++)
                if (tuning.Notes[i] == target) { matched = i; break; }
        }

        if (abs <= 3)
        {
            inTuneSeconds += dt;
            if (inTuneSeconds > 0.5)
            {
                HintText.Text = "✓  Accordata!";
                HintText.Foreground = Good;
                if (matched >= 0 && matched < stringButtons.Count) stringButtons[matched].Tuned = true;
            }
            else { HintText.Text = "Quasi…"; HintText.Foreground = Good; }
        }
        else
        {
            inTuneSeconds = 0;
            if (cents < 0)
            {
                HintText.Text = abs > 50 ? "⬆  Molto bassa: tendi la corda" : "⬆  Bassa: tendi un po' la corda";
            }
            else
            {
                HintText.Text = abs > 50 ? "⬇  Molto alta: allenta la corda" : "⬇  Alta: allenta un po' la corda";
            }
            HintText.Foreground = color;
        }

        UpdateStringVisuals(matched);
    }

    void UpdateStringVisuals(int active)
    {
        for (int i = 0; i < stringButtons.Count; i++)
        {
            var sb = stringButtons[i];
            bool isLocked = i == lockedString;
            bool isActive = i == active;
            sb.Button.BorderBrush = isLocked ? Accent : (isActive ? Good : Brushes.Transparent);
            sb.Note.Foreground = isActive ? Good : (sb.Tuned ? Good : TextBrush);
            string baseLabel = sb.Label.Text.TrimStart('✓', ' ');
            sb.Label.Text = sb.Tuned ? "✓ " + baseLabel : baseLabel;
            sb.Label.Foreground = sb.Tuned ? Good : Muted;
        }
    }

    void UpdateLevel(double dt)
    {
        if (!input.IsRunning)
        {
            LevelBar.Width = 0;
            LevelText.Text = "—";
            InputInfoText.Text = InputCombo.SelectedItem is DeviceItem d ? $"{d.Name} — non attivo" : InputInfoText.Text;
            return;
        }

        float peak = input.Peak;
        double db = peak > 0 ? 20 * Math.Log10(peak) : -90;
        // fast attack, slow release
        levelDisplay = db > levelDisplay ? db : levelDisplay - dt * 30;
        if (levelDisplay < -90) levelDisplay = -90;

        var track = (FrameworkElement)LevelBar.Parent;
        double frac = Math.Clamp((levelDisplay + 60) / 60.0, 0, 1);
        double w = Math.Round(Math.Max(0, track.ActualWidth * frac));
        if (LevelBar.Width != w) LevelBar.Width = w;
        LevelBar.Background = levelDisplay > -3 ? Bad : levelDisplay > -12 ? Warn : Good;
        LevelText.Text = levelDisplay <= -89 ? "-∞ dB" : $"{levelDisplay:0} dB";

        bool clip = (DateTime.UtcNow - input.LastClipUtc).TotalSeconds < 1.5;
        ClipLight.Fill = clip ? Bad : ClipOff;

        string ch = input.ActiveChannel switch
        {
            0 => input.Channels > 1 ? "Ingresso 1 (sinistro)" : "mono",
            1 => "Ingresso 2 (destro / strumento)",
            _ => "Mix 1+2",
        };
        string name = InputCombo.SelectedItem is DeviceItem di ? di.Name : "";
        string hint = levelDisplay < -45 ? "  ·  segnale debole: alza il GAIN sulla Scarlett" : clip ? "  ·  ⚠ saturazione: abbassa il GAIN" : "";
        InputInfoText.Text = $"{name}  ·  {ch}{(input.Mode == ChannelMode.Auto ? " (auto)" : "")}{hint}";
    }

    // =====================================================================
    // Metronome
    // =====================================================================

    static string TempoName(double bpm) => bpm switch
    {
        < 40 => "Grave",
        < 60 => "Largo",
        < 66 => "Larghetto",
        < 76 => "Adagio",
        < 108 => "Andante",
        < 120 => "Moderato",
        < 156 => "Allegro",
        < 176 => "Vivace",
        < 200 => "Presto",
        _ => "Prestissimo",
    };

    void SetBpm(double bpm)
    {
        bpm = Math.Round(Math.Clamp(bpm, BpmSlider.Minimum, BpmSlider.Maximum));
        Metro.Bpm = bpm;
        suppressBpm = true;
        BpmSlider.Value = bpm;
        suppressBpm = false;
        UpdateBpmText();
    }

    void UpdateBpmText()
    {
        BpmText.Text = $"{Metro.Bpm:0}";
        TempoNameText.Text = $"BPM · {TempoName(Metro.Bpm)}";
    }

    void BpmSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!loaded || suppressBpm) return;
        Metro.Bpm = Math.Round(BpmSlider.Value);
        UpdateBpmText();
    }

    void BpmMinus5_Click(object sender, RoutedEventArgs e) => SetBpm(Metro.Bpm - 5);
    void BpmMinus1_Click(object sender, RoutedEventArgs e) => SetBpm(Metro.Bpm - 1);
    void BpmPlus1_Click(object sender, RoutedEventArgs e) => SetBpm(Metro.Bpm + 1);
    void BpmPlus5_Click(object sender, RoutedEventArgs e) => SetBpm(Metro.Bpm + 5);
    void Bpm_MouseWheel(object sender, MouseWheelEventArgs e) => SetBpm(Metro.Bpm + Math.Sign(e.Delta));

    void Tap_Click(object sender, RoutedEventArgs e) => Tap();

    void Tap()
    {
        double t = tapWatch.Elapsed.TotalSeconds;
        if (tapTimes.Count > 0 && t - tapTimes[^1] > 2.0) tapTimes.Clear();
        tapTimes.Add(t);
        if (tapTimes.Count > 6) tapTimes.RemoveAt(0);
        if (tapTimes.Count >= 2)
        {
            double avg = (tapTimes[^1] - tapTimes[0]) / (tapTimes.Count - 1);
            if (avg > 0) SetBpm(60.0 / avg);
            StatusText.Text = $"Tap tempo: {Metro.Bpm:0} BPM ({tapTimes.Count} tocchi)";
        }
        else StatusText.Text = "Tap tempo: continua a toccare a tempo…";
    }

    void StartStop_Click(object sender, RoutedEventArgs e) => ToggleMetronome();

    void ToggleMetronome()
    {
        if (Metro.IsRunning) Metro.Stop();
        else
        {
            if (output.DeviceName.Length == 0)
            {
                StatusText.Text = "Nessuna uscita audio attiva: scegline una in Impostazioni audio.";
                return;
            }
            Metro.Start();
            if (!session.IsRunning) { session.Start(); SessionButton.Content = "Pausa"; }
        }
        lastEventId = -1;
        UpdateStartButton();
    }

    void UpdateStartButton()
    {
        StartButton.Content = Metro.IsRunning ? "■   Ferma  (Spazio)" : "▶   Avvia  (Spazio)";
        if (!Metro.IsRunning) BarText.Text = " ";
    }

    void BeatsCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!loaded) return;
        Metro.BeatsPerBar = BeatsCombo.SelectedIndex + 1;
        RebuildBeatDots();
    }

    void SubdivCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!loaded) return;
        if (SubdivCombo.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out int s))
            Metro.Subdivision = s;
    }

    void SoundCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!loaded) return;
        Metro.Sound = (ClickSound)Math.Max(0, SoundCombo.SelectedIndex);
        if (!Metro.IsRunning) Metro.Preview();
    }

    void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!loaded) return;
        Metro.Volume = (float)(VolumeSlider.Value / 100);
        VolumeText.Text = $"{VolumeSlider.Value:0}%";
    }

    void Trainer_Changed(object sender, RoutedEventArgs e) { if (loaded) ApplyTrainer(); }

    void ApplyTrainer()
    {
        Metro.TrainerEnabled = TrainerCheck.IsChecked == true;
        Metro.TrainerTarget = TrainerTargetSlider.Value;
        Metro.TrainerStep = TrainerStepSlider.Value;
        Metro.TrainerEveryBars = (int)TrainerEverySlider.Value;
        TrainerTargetText.Text = $"{TrainerTargetSlider.Value:0}";
        TrainerStepText.Text = $"±{TrainerStepSlider.Value:0}";
        TrainerEveryText.Text = $"{TrainerEverySlider.Value:0}";
    }

    void Gap_Changed(object sender, RoutedEventArgs e) { if (loaded) ApplyGap(); }

    void ApplyGap()
    {
        Metro.GapEnabled = GapCheck.IsChecked == true;
        Metro.GapPlayBars = (int)GapPlaySlider.Value;
        Metro.GapMuteBars = (int)GapMuteSlider.Value;
        GapPlayText.Text = $"{GapPlaySlider.Value:0}";
        GapMuteText.Text = $"{GapMuteSlider.Value:0}";
    }

    void RebuildBeatDots()
    {
        BeatDotsPanel.Children.Clear();
        beatDots.Clear();
        for (int i = 0; i < Metro.BeatsPerBar; i++)
        {
            var scale = new ScaleTransform(1, 1);
            var dot = new Border
            {
                Width = 40, Height = 40, CornerRadius = new CornerRadius(20),
                Margin = new Thickness(6), Tag = i, Cursor = Cursors.Hand,
                RenderTransform = scale, RenderTransformOrigin = new Point(0.5, 0.5),
                BorderThickness = new Thickness(2),
                Child = new TextBlock
                {
                    Text = (i + 1).ToString(), HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold,
                },
            };
            dot.MouseLeftButtonDown += BeatDot_Click;
            BeatDotsPanel.Children.Add(dot);
            beatDots.Add((dot, scale));
        }
        UpdateBeatDotColors();
    }

    void BeatDot_Click(object sender, MouseButtonEventArgs e)
    {
        int i = (int)((Border)sender).Tag;
        Metro.Accents[i] = (AccentLevel)(((int)Metro.Accents[i] + 1) % 3);
        UpdateBeatDotColors();
    }

    void UpdateBeatDotColors()
    {
        for (int i = 0; i < beatDots.Count; i++)
        {
            var (dot, _) = beatDots[i];
            var tb = (TextBlock)dot.Child;
            switch (Metro.Accents[i])
            {
                case AccentLevel.Accent:
                    dot.Background = Accent; dot.BorderBrush = Accent; tb.Foreground = Brushes.Black;
                    dot.ToolTip = "Accento"; break;
                case AccentLevel.Normal:
                    dot.Background = Muted; dot.BorderBrush = Muted; tb.Foreground = Brushes.Black;
                    dot.ToolTip = "Normale"; break;
                default:
                    dot.Background = Brushes.Transparent; dot.BorderBrush = Faint; tb.Foreground = Muted;
                    dot.ToolTip = "Muto"; break;
            }
            dot.Opacity = 0.55;
        }
    }

    void UpdateMetronome(double dt, bool visible)
    {
        // speed trainer may change BPM from the audio thread
        if (Math.Abs(Metro.Bpm - BpmSlider.Value) > 0.01)
        {
            suppressBpm = true;
            BpmSlider.Value = Metro.Bpm;
            suppressBpm = false;
            UpdateBpmText();
        }

        if (!visible) return;

        double k = Math.Min(1, dt * 9);
        foreach (var (dot, scale) in beatDots)
        {
            if (scale.ScaleX != 1)
            {
                double s = scale.ScaleX + (1 - scale.ScaleX) * k;
                if (Math.Abs(s - 1) < 0.004) s = 1;
                scale.ScaleX = scale.ScaleY = s;
            }
            if (dot.Opacity != 0.55)
            {
                double o = dot.Opacity + (0.55 - dot.Opacity) * k;
                dot.Opacity = Math.Abs(o - 0.55) < 0.005 ? 0.55 : o;
            }
        }
        if (FlashOverlay.Opacity != 0)
        {
            double fo = FlashOverlay.Opacity * (1 - Math.Min(1, dt * 7));
            FlashOverlay.Opacity = fo < 0.004 ? 0 : fo;
        }

        if (!Metro.IsRunning) return;

        if (Metro.TryGetLatest(output.PlayedFrames, out var ev) && ev.Id != lastEventId)
        {
            lastEventId = ev.Id;
            if (ev.Beat < beatDots.Count)
            {
                var (dot, scale) = beatDots[ev.Beat];
                if (ev.Sub == 0)
                {
                    scale.ScaleX = scale.ScaleY = 1.35;
                    dot.Opacity = 1;
                    if (ev.Beat == 0 && !ev.Muted) FlashOverlay.Opacity = 0.18;
                }
                else
                {
                    scale.ScaleX = scale.ScaleY = Math.Max(scale.ScaleX, 1.12);
                }
            }
            string bar = $"Battuta {ev.Bar + 1}";
            if (ev.Muted) bar += "  ·  🤫 battuta muta: tieni il tempo!";
            if (Metro.TrainerEnabled) bar += $"  ·  allenatore: {Metro.Bpm:0} → {Metro.TrainerTarget:0} BPM";
            BarText.Text = bar;
        }
    }

    // =====================================================================
    // Session timer
    // =====================================================================

    void UpdateSession()
    {
        var t = session.Elapsed;
        SessionText.Text = $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";
    }

    void SessionButton_Click(object sender, RoutedEventArgs e)
    {
        if (session.IsRunning) { session.Stop(); SessionButton.Content = "Riprendi"; }
        else { session.Start(); SessionButton.Content = "Pausa"; }
    }

    void SessionReset_Click(object sender, RoutedEventArgs e)
    {
        bool running = session.IsRunning;
        session.Reset();
        if (running) session.Start();
        else SessionButton.Content = "Avvia";
    }

    // =====================================================================
    // Keyboard shortcuts
    // =====================================================================

    void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox) return;
        bool inSelector = Keyboard.FocusedElement is ComboBox or ComboBoxItem;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        switch (e.Key)
        {
            case Key.Space:
                ToggleMetronome(); e.Handled = true; break;
            case Key.Up when !inSelector:
                SetBpm(Metro.Bpm + (shift ? 5 : 1)); e.Handled = true; break;
            case Key.Down when !inSelector:
                SetBpm(Metro.Bpm - (shift ? 5 : 1)); e.Handled = true; break;
            case Key.T:
                Tap(); e.Handled = true; break;
            case Key.F1: Tabs.SelectedIndex = 0; e.Handled = true; break;
            case Key.F2: Tabs.SelectedIndex = 1; e.Handled = true; break;
            case Key.F3: Tabs.SelectedIndex = 2; e.Handled = true; break;
            case Key.F4: Tabs.SelectedIndex = 3; e.Handled = true; break;
        }
    }
}
