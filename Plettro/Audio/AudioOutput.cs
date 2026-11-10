using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Plettro.Audio;

/// <summary>Owns the WASAPI output: metronome + reference tone mixed together.</summary>
public sealed class AudioOutput : IDisposable
{
    WasapiOut? output;
    public MetronomeProvider Metronome { get; } = new();
    public ToneProvider Tone { get; } = new();
    public int SampleRate { get; private set; } = 48000;
    public string DeviceName { get; private set; } = "";

    public void Start(MMDevice device)
    {
        Stop();
        int rate;
        try { rate = device.AudioClient.MixFormat.SampleRate; }
        catch { rate = 48000; }
        SampleRate = rate;

        Metronome.Configure(rate);
        Tone.Configure(rate);

        var mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(rate, 2)) { ReadFully = true };
        mixer.AddMixerInput(Metronome);
        mixer.AddMixerInput(Tone);

        var o = new WasapiOut(device, AudioClientShareMode.Shared, true, 40);
        o.Init(mixer);
        o.Play();
        output = o;
        DeviceName = device.FriendlyName;
    }

    /// <summary>Frames actually played by the device (audio clock), in our sample-rate units.</summary>
    public long PlayedFrames
    {
        get
        {
            var o = output;
            if (o == null) return 0;
            try
            {
                long bytes = o.GetPosition();
                double seconds = bytes / (double)o.OutputWaveFormat.AverageBytesPerSecond;
                return (long)(seconds * SampleRate);
            }
            catch { return 0; }
        }
    }

    public void Stop()
    {
        var o = output;
        output = null;
        if (o == null) return;
        try { o.Stop(); } catch { }
        try { o.Dispose(); } catch { }
    }

    public void Dispose() => Stop();
}
