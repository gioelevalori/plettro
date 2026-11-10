using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using Plettro.Audio;
using Plettro.Audio.Fx;

namespace Plettro;

/// <summary>"Effetti" tab: live amp simulator built in code.</summary>
public partial class MainWindow
{
    const string WasapiOption = "WASAPI (latenza più alta, senza driver ASIO)";

    readonly LiveEngine live = new();
    RigSettings rig = new();
    Convolver? loadedIr;
    int loadedIrRate;
    readonly List<Action> fxRefreshers = new();
    bool fxUpdating;

    ComboBox fxDriverCombo = null!, fxChannelCombo = null!, fxPresetCombo = null!;
    Button fxPowerButton = null!, fxAsioPanelButton = null!;
    TextBlock fxStatus = null!, fxPresetDesc = null!, fxIrText = null!;
    Border fxInBar = null!, fxOutBar = null!;
    Ellipse fxClip = null!;
    double fxInDisplay = -90, fxOutDisplay = -90;
    DateTime fxLastClip;

    // =====================================================================
    // Build UI
    // =====================================================================

    void BuildFxTab()
    {
        rig = settings.Rig?.Clone() ?? RigPresets.All[0].Settings.Clone();

        // ---------- top bar: driver, input, power ----------
        var top = new Border { Style = (Style)FindResource("Card"), Margin = new Thickness(0, 0, 0, 10) };
        var topGrid = new Grid();
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new WrapPanel();
        fxDriverCombo = new ComboBox { Width = 320 };
        foreach (var d in LiveEngine.AsioDrivers()) fxDriverCombo.Items.Add(d);
        fxDriverCombo.Items.Add(WasapiOption);
        fxDriverCombo.SelectedItem =
            fxDriverCombo.Items.Cast<string>().FirstOrDefault(d => d == settings.FxDriver)
            ?? fxDriverCombo.Items.Cast<string>().FirstOrDefault(d => d.Contains("Focusrite USB", StringComparison.OrdinalIgnoreCase))
            ?? fxDriverCombo.Items.Cast<string>().FirstOrDefault(d => d.Contains("Focusrite", StringComparison.OrdinalIgnoreCase))
            ?? fxDriverCombo.Items[0];
        fxDriverCombo.SelectionChanged += (_, _) => { if (live.IsRunning) RestartLive(); UpdateFxHints(); };
        left.Children.Add(Labeled("Driver audio", fxDriverCombo));

        fxChannelCombo = new ComboBox { Width = 200 };
        fxChannelCombo.Items.Add("Ingresso 1 (mic / linea)");
        fxChannelCombo.Items.Add("Ingresso 2 (chitarra, INST)");
        fxChannelCombo.SelectedIndex = Math.Clamp(settings.FxInputChannel, 0, 1);
        fxChannelCombo.SelectionChanged += (_, _) => { if (live.IsRunning) RestartLive(); };
        left.Children.Add(Labeled("Ingresso chitarra", fxChannelCombo));

        fxAsioPanelButton = new Button { Content = "Pannello ASIO (buffer)", Focusable = false, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 6) };
        fxAsioPanelButton.Click += (_, _) => live.ShowAsioPanel();
        left.Children.Add(fxAsioPanelButton);
        topGrid.Children.Add(left);

        fxPowerButton = new Button
        {
            Content = "⏻  Accendi ampli", FontSize = 18, Padding = new Thickness(28, 10, 28, 10),
            Focusable = false, VerticalAlignment = VerticalAlignment.Center,
        };
        fxPowerButton.SetResourceReference(StyleProperty, "AccentButtonStyle");
        fxPowerButton.Click += (_, _) => { if (live.IsRunning) StopLive(); else StartLive(); };
        Grid.SetColumn(fxPowerButton, 1);
        topGrid.Children.Add(fxPowerButton);

        var topStack = new StackPanel();
        topStack.Children.Add(topGrid);
        fxStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Muted, Margin = new Thickness(0, 6, 0, 0) };
        topStack.Children.Add(fxStatus);

        // meters
        var meters = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        meters.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        meters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        meters.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        meters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        meters.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        meters.Children.Add(new TextBlock { Text = "In", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        var inTrack = MeterTrack(out fxInBar); Grid.SetColumn(inTrack, 1); meters.Children.Add(inTrack);
        var outLabel = new TextBlock { Text = "Out", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 8, 0) };
        Grid.SetColumn(outLabel, 2); meters.Children.Add(outLabel);
        var outTrack = MeterTrack(out fxOutBar); Grid.SetColumn(outTrack, 3); meters.Children.Add(outTrack);
        var clipPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        fxClip = new Ellipse { Width = 12, Height = 12, Fill = ClipOff };
        clipPanel.Children.Add(fxClip);
        clipPanel.Children.Add(new TextBlock { Text = "CLIP", Margin = new Thickness(6, 0, 0, 0), Foreground = Muted });
        Grid.SetColumn(clipPanel, 4); meters.Children.Add(clipPanel);
        topStack.Children.Add(meters);
        top.Child = topStack;
        FxRoot.Children.Add(top);

        // ---------- presets ----------
        var presetCard = new Border { Style = (Style)FindResource("Card"), Margin = new Thickness(0, 0, 0, 10) };
        var presetStack = new StackPanel();
        var presetRow = new DockPanel();
        var presetLabel = new TextBlock { Text = "Preset", FontSize = 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        presetRow.Children.Add(presetLabel);
        fxPresetCombo = new ComboBox { MinWidth = 300 };
        foreach (var p in RigPresets.All) fxPresetCombo.Items.Add(p.Name);
        fxPresetCombo.Items.Add("Personalizzato");
        fxPresetCombo.SelectedIndex = fxPresetCombo.Items.Count - 1;
        fxPresetCombo.SelectionChanged += (_, _) =>
        {
            if (fxUpdating) return;
            int i = fxPresetCombo.SelectedIndex;
            if (i < 0 || i >= RigPresets.All.Length) return;
            var keepIr = rig.IrPath;
            rig = RigPresets.All[i].Settings.Clone();
            rig.IrPath = keepIr;
            fxPresetDesc.Text = RigPresets.All[i].Description;
            RefreshFxControls();
            ApplyRig();
        };
        presetRow.Children.Add(fxPresetCombo);
        presetStack.Children.Add(presetRow);
        fxPresetDesc = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Muted, Margin = new Thickness(0, 6, 0, 0), Text = "Scegli un preset come punto di partenza, poi regola le manopole a piacere." };
        presetStack.Children.Add(fxPresetDesc);
        presetCard.Child = presetStack;
        FxRoot.Children.Add(presetCard);

        // ---------- pedalboard ----------
        var board = new WrapPanel();
        FxRoot.Children.Add(board);

        var gate = Pedal(board, "Noise gate", () => rig.GateOn, v => rig.GateOn = v);
        Knob(gate, "Soglia", -80, -30, () => rig.GateThresholdDb, v => rig.GateThresholdDb = v, v => $"{v:0} dB");
        gate.Children.Add(Hint("Toglie il fruscio tra una pennata e l'altra. Alza la soglia con i suoni molto distorti."));

        var od = Pedal(board, "Overdrive", () => rig.DriveOn, v => rig.DriveOn = v);
        Knob(od, "Drive", 0, 10, () => rig.Drive, v => rig.Drive = v, Num);
        Knob(od, "Tono", 0, 10, () => rig.DriveTone, v => rig.DriveTone = v, Num);
        Knob(od, "Livello", 0, 10, () => rig.DriveLevel, v => rig.DriveLevel = v, Num);

        var amp = Pedal(board, "Amplificatore", null, null, 290);
        var model = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 8) };
        model.Items.Add("Clean (pulito americano)");
        model.Items.Add("Crunch (britannico)");
        model.Items.Add("Lead (britannico modificato)");
        model.Items.Add("Metal (high gain moderno)");
        model.SelectionChanged += (_, _) => { if (fxUpdating) return; rig.Model = (AmpModel)model.SelectedIndex; Customized(); ApplyRig(); };
        fxRefreshers.Add(() => model.SelectedIndex = (int)rig.Model);
        amp.Children.Add(model);
        Knob(amp, "Gain", 0, 10, () => rig.Gain, v => rig.Gain = v, Num);
        Knob(amp, "Bassi", 0, 10, () => rig.Bass, v => rig.Bass = v, Num);
        Knob(amp, "Medi", 0, 10, () => rig.Mid, v => rig.Mid = v, Num);
        Knob(amp, "Alti", 0, 10, () => rig.Treble, v => rig.Treble = v, Num);
        Knob(amp, "Presence", 0, 10, () => rig.Presence, v => rig.Presence = v, Num);
        Knob(amp, "Master", 0, 10, () => rig.Master, v => rig.Master = v, Num);

        var cab = Pedal(board, "Cassa (cabinet)", null, null);
        var cabCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 8) };
        cabCombo.Items.Add("Nessuna (diretto)");
        cabCombo.Items.Add("4x12 chiusa (rock / punk)");
        cabCombo.Items.Add("2x12 aperta");
        cabCombo.Items.Add("1x12 combo");
        cabCombo.Items.Add("IR da file (.wav)");
        cabCombo.SelectionChanged += (_, _) =>
        {
            if (fxUpdating) return;
            rig.Cab = (CabType)cabCombo.SelectedIndex;
            if (rig.Cab == CabType.ImpulseResponse && string.IsNullOrEmpty(rig.IrPath)) ChooseIr();
            Customized(); ApplyRig();
        };
        fxRefreshers.Add(() => cabCombo.SelectedIndex = (int)rig.Cab);
        cab.Children.Add(cabCombo);
        var irButton = new Button { Content = "Carica IR…", Focusable = false, HorizontalAlignment = HorizontalAlignment.Left };
        irButton.Click += (_, _) => { ChooseIr(); };
        cab.Children.Add(irButton);
        fxIrText = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Muted, FontSize = 12, Margin = new Thickness(0, 6, 0, 0) };
        cab.Children.Add(fxIrText);
        cab.Children.Add(Hint("Gli IR sono file .wav che riproducono una cassa vera ripresa con un microfono: ne trovi molti gratuiti online. Danno il suono più realistico."));

        var delay = Pedal(board, "Delay", () => rig.DelayOn, v => rig.DelayOn = v);
        Knob(delay, "Tempo", 60, 1200, () => rig.DelayMs, v => rig.DelayMs = v, v => $"{v:0} ms");
        Knob(delay, "Ripetizioni", 0, 10, () => rig.DelayFeedback, v => rig.DelayFeedback = v, Num);
        Knob(delay, "Mix", 0, 10, () => rig.DelayMix, v => rig.DelayMix = v, Num);

        var rev = Pedal(board, "Riverbero", () => rig.ReverbOn, v => rig.ReverbOn = v);
        Knob(rev, "Ambiente", 0, 10, () => rig.ReverbSize, v => rig.ReverbSize = v, Num);
        Knob(rev, "Mix", 0, 10, () => rig.ReverbMix, v => rig.ReverbMix = v, Num);

        var outp = Pedal(board, "Uscita", null, null);
        Knob(outp, "Volume", -24, 12, () => rig.OutputDb, v => rig.OutputDb = v, v => $"{v:+0;-0;0} dB");

        fxUpdating = true;
        if (settings.Rig == null)
        {
            fxPresetCombo.SelectedIndex = 0;
            fxPresetDesc.Text = RigPresets.All[0].Description;
        }
        RefreshFxControls();
        fxUpdating = false;
        UpdateFxHints();
    }

    static string Num(double v) => v.ToString("0.0");

    static FrameworkElement Labeled(string label, FrameworkElement control)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 0, 16, 6) };
        sp.Children.Add(new TextBlock { Text = label, Foreground = Muted, FontSize = 12, Margin = new Thickness(0, 0, 0, 4) });
        sp.Children.Add(control);
        return sp;
    }

    static TextBlock Hint(string text) =>
        new() { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Muted, FontSize = 12, Margin = new Thickness(0, 8, 0, 0) };

    static Grid MeterTrack(out Border bar)
    {
        var g = new Grid { Height = 12, VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(new Border { CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)) });
        bar = new Border { CornerRadius = new CornerRadius(6), HorizontalAlignment = HorizontalAlignment.Left, Width = 0, Background = Good };
        g.Children.Add(bar);
        return g;
    }

    StackPanel Pedal(Panel board, string title, Func<bool>? getOn, Action<bool>? setOn, double width = 250)
    {
        var card = new Border { Style = (Style)FindResource("Card"), Width = width, Margin = new Thickness(0, 0, 10, 10), VerticalAlignment = VerticalAlignment.Top };
        var stack = new StackPanel();
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        if (getOn != null && setOn != null)
        {
            var toggle = new CheckBox { Content = "ON", Focusable = false, MinWidth = 0 };
            DockPanel.SetDock(toggle, Dock.Right);
            toggle.Click += (_, _) => { setOn(toggle.IsChecked == true); Customized(); ApplyRig(); UpdatePedalLook(card, toggle.IsChecked == true); };
            fxRefreshers.Add(() => { toggle.IsChecked = getOn(); UpdatePedalLook(card, getOn()); });
            header.Children.Add(toggle);
        }
        header.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        stack.Children.Add(header);
        card.Child = stack;
        board.Children.Add(card);
        return stack;
    }

    static void UpdatePedalLook(Border card, bool on)
    {
        card.BorderBrush = on ? Accent : new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
        card.Opacity = on ? 1 : 0.75;
    }

    void Knob(Panel parent, string label, double min, double max, Func<double> get, Action<double> set, Func<double, string> fmt)
    {
        var value = new TextBlock { Width = 64, TextAlignment = TextAlignment.Right };
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 0) };
        DockPanel.SetDock(value, Dock.Right);
        row.Children.Add(value);
        row.Children.Add(new TextBlock { Text = label });
        var slider = new Slider { Minimum = min, Maximum = max, Focusable = false, Margin = new Thickness(0, 0, 0, 4) };
        slider.ValueChanged += (_, _) =>
        {
            value.Text = fmt(slider.Value);
            if (fxUpdating) return;
            set(slider.Value);
            Customized();
            ApplyRig();
        };
        fxRefreshers.Add(() => { slider.Value = get(); value.Text = fmt(get()); });
        parent.Children.Add(row);
        parent.Children.Add(slider);
    }

    void RefreshFxControls()
    {
        bool was = fxUpdating;
        fxUpdating = true;
        foreach (var r in fxRefreshers) r();
        fxIrText.Text = string.IsNullOrEmpty(rig.IrPath) ? "Nessun IR caricato." : "IR: " + System.IO.Path.GetFileName(rig.IrPath);
        fxUpdating = was;
    }

    void Customized()
    {
        if (fxUpdating) return;
        fxUpdating = true;
        fxPresetCombo.SelectedIndex = fxPresetCombo.Items.Count - 1;
        fxPresetDesc.Text = "Impostazioni personalizzate (vengono salvate automaticamente).";
        fxUpdating = false;
    }

    // =====================================================================
    // Engine control
    // =====================================================================

    bool UsingAsio => fxDriverCombo.SelectedItem is string s && s != WasapiOption;

    void UpdateFxHints()
    {
        fxAsioPanelButton.Visibility = UsingAsio ? Visibility.Visible : Visibility.Collapsed;
        if (live.IsRunning) return;
        bool hasAsio = fxDriverCombo.Items.Count > 1;
        fxStatus.Text = hasAsio
            ? "Ampli spento. Prima di accenderlo spegni DIRECT MONITOR sulla Scarlett, altrimenti senti anche il suono pulito. Usa le cuffie o le casse collegate alla Scarlett."
            : "Nessun driver ASIO trovato: installa \"Focusrite Control\" (che contiene il driver Focusrite USB ASIO) per avere la latenza minima. Intanto puoi usare WASAPI.";
    }

    void StartLive()
    {
        try
        {
            int ch = fxChannelCombo.SelectedIndex;
            if (UsingAsio)
            {
                live.StartAsio((string)fxDriverCombo.SelectedItem, ch, ConfigureRig);
            }
            else
            {
                if (InputCombo.SelectedItem is not DeviceItem inItem || OutputCombo.SelectedItem is not DeviceItem outItem)
                {
                    fxStatus.Text = "Scegli ingresso e uscita in Impostazioni audio.";
                    return;
                }
                live.StartWasapi(enumerator.GetDevice(inItem.Id), enumerator.GetDevice(outItem.Id), ch, ConfigureRig);
            }
            fxPowerButton.Content = "⏻  Spegni ampli";
            fxStatus.Text = $"🔥 Ampli acceso · {live.Description} · latenza ≈ {live.LatencyMs:0} ms" +
                            (UsingAsio ? "" : "  ·  Con WASAPI si sente un po' di ritardo: per suonare meglio usa il driver ASIO.") +
                            "\nRicorda: DIRECT MONITOR spento sulla Scarlett.";
        }
        catch (Exception ex)
        {
            live.Stop();
            fxPowerButton.Content = "⏻  Accendi ampli";
            fxStatus.Text = "Impossibile accendere l'ampli: " + ex.Message +
                            "\nChiudi altri programmi che usano la scheda audio (DAW, altri simulatori) e riprova, oppure prova un altro driver.";
        }
    }

    void StopLive()
    {
        live.Stop();
        fxPowerButton.Content = "⏻  Accendi ampli";
        UpdateFxHints();
    }

    void RestartLive()
    {
        StopLive();
        StartLive();
    }

    void ConfigureRig(GuitarRig r)
    {
        EnsureIr(live.SampleRate);
        r.ImpulseResponse = loadedIr;
        rig.ApplyTo(r);
    }

    void ApplyRig()
    {
        var r = live.Rig;
        if (r == null) return;
        r.ImpulseResponse = loadedIr;
        rig.ApplyTo(r);
    }

    void EnsureIr(int rate)
    {
        if (string.IsNullOrEmpty(rig.IrPath)) { loadedIr = null; return; }
        if (loadedIr != null && loadedIrRate == rate) return;
        try
        {
            loadedIr = Convolver.Load(rig.IrPath, rate > 0 ? rate : 48000);
            loadedIrRate = rate;
        }
        catch (Exception ex)
        {
            loadedIr = null;
            fxIrText.Text = "Errore nel caricare l'IR: " + ex.Message;
        }
    }

    void ChooseIr()
    {
        var dlg = new OpenFileDialog { Filter = "Impulse response (*.wav)|*.wav", Title = "Scegli un IR di cassa" };
        if (dlg.ShowDialog(this) != true)
        {
            if (string.IsNullOrEmpty(rig.IrPath)) { rig.Cab = CabType.Cab4x12; RefreshFxControls(); }
            return;
        }
        rig.IrPath = dlg.FileName;
        rig.Cab = CabType.ImpulseResponse;
        loadedIr = null;
        EnsureIr(live.IsRunning ? live.SampleRate : 48000);
        RefreshFxControls();
        Customized();
        ApplyRig();
    }

    void SaveFxSettings()
    {
        settings.Rig = rig.Clone();
        if (fxDriverCombo != null) settings.FxDriver = fxDriverCombo.SelectedItem as string;
        if (fxChannelCombo != null) settings.FxInputChannel = fxChannelCombo.SelectedIndex;
    }

    void UpdateFxMeters(double dt)
    {
        var r = live.Rig;
        float pin = r?.PeakIn ?? 0, pout = r?.PeakOut ?? 0;
        if (pin >= 0.98f) fxLastClip = DateTime.UtcNow;
        Meter(fxInBar, pin, ref fxInDisplay, dt);
        Meter(fxOutBar, pout, ref fxOutDisplay, dt);
        fxClip.Fill = (DateTime.UtcNow - fxLastClip).TotalSeconds < 1.5 ? Bad : ClipOff;
    }

    static void Meter(Border bar, float peak, ref double display, double dt)
    {
        double db = peak > 0 ? 20 * Math.Log10(peak) : -90;
        display = db > display ? db : Math.Max(-90, display - dt * 30);
        var track = (FrameworkElement)bar.Parent;
        double w = Math.Round(track.ActualWidth * Math.Clamp((display + 60) / 60, 0, 1));
        if (bar.Width != w) bar.Width = w;
        bar.Background = display > -3 ? Bad : display > -12 ? Warn : Good;
    }
}
