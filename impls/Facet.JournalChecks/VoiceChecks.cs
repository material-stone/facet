using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Facet;
using Facet.ChatPlugin;

internal static partial class Program
{
    private static void CheckVoiceAgent(Controller controller, string qa)
    {
        Assert(controller.SetPluginHotkey("facet.chat", "voice", "Win+Alt+V") != null, "only a running plugin's own hotkey can be rebound");
        var pcm = new byte[16000 * 2]; for (int i = 0; i < 8000; i++) BitConverter.GetBytes((short)(i % 40 < 20 ? 12000 : -12000)).CopyTo(pcm, i * 2);
        using var wav = new MemoryStream(); using (var w = new BinaryWriter(wav, System.Text.Encoding.ASCII, true)) {
            w.Write("RIFF"u8); w.Write(36 + pcm.Length); w.Write("WAVEfmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(16000); w.Write(32000); w.Write((short)2); w.Write((short)16); w.Write("data"u8); w.Write(pcm.Length); w.Write(pcm);
        }
        var frames = VoiceSpeaker.Spectra(wav.ToArray(), out double seconds);
        Assert(Math.Abs(seconds - 1) < .01 && frames.Length == 20 && frames[0].Max() > .5 && frames[^1].Max() == 0, "speech spectra follow loud then silent PCM frames");
        float[] Tone(double hz) => Enumerable.Range(0, VoiceSpectrum.Size).Select(i => (float)(.3 * Math.Sin(2 * Math.PI * hz * i / 16000))).ToArray();
        int Peak(double[] bands) => Array.IndexOf(bands, bands.Max());
        int low = Peak(VoiceSpectrum.Analyze(Tone(150), 16000)), mid = Peak(VoiceSpectrum.Analyze(Tone(1000), 16000)), high = Peak(VoiceSpectrum.Analyze(Tone(5000), 16000));
        foreach (int rate in new[] { 16000, 22050, 44100 }) {
            var layout = VoiceSpectrum.Layout(rate);
            Assert(layout.Zip(layout.Skip(1)).All(p => p.Second.First > p.First.Last || p.First.Last == VoiceSpectrum.Size / 2 - 1), "no two equalizer bands read the same FFT bins at " + rate + " Hz");
        }
        var follower = new VoiceSpectrumFollower(); var loudLow = Enumerable.Range(0, VoiceSpectrum.Bands).Select(b => b < 10 ? .9 : .4).ToArray();
        // Speech alternates with pauses, so the noise floor stays low while each band's peak is learned.
        double[] shown = follower.Next(new double[VoiceSpectrum.Bands], .05);
        for (int i = 0; i < 20; i++) shown = follower.Next(i % 2 == 0 ? new double[VoiceSpectrum.Bands] : loudLow, .05);
        Assert(shown[30] > .7 && shown[30] / shown[0] > .75 && shown[0] > shown[30], "per-band gain lets steady quieter bands rise close to the loud fundamental");
        Assert(new VoiceSpectrumFollower().Next(new double[VoiceSpectrum.Bands], .05).Max() == 0, "silence stays flat after per-band gain");
        var room = new VoiceSpectrumFollower(); double[] hum = Enumerable.Repeat(.3, VoiceSpectrum.Bands).ToArray(), heard = hum;
        for (int i = 0; i < 100; i++) heard = room.Next(hum, .05);
        Assert(heard.Max() < .05, "steady room noise sinks to the band noise floor");
        float[] Tone44(double hz) => Enumerable.Range(0, 44100).Select(i => (float)(.3 * Math.Sin(2 * Math.PI * hz * i / 44100))).ToArray();
        foreach (double hz in new[] { 150.0, 1000, 5000 }) {
            var micBands = VoiceSpectrum.Analyze(Tone(hz), 16000); var ttsBands = VoiceSpectrum.Analyze(VoiceSpectrum.Resample(Tone44(hz), 44100, 16000), 16000);
            Assert(Math.Abs(Peak(micBands) - Peak(ttsBands)) <= 1, $"a {hz} Hz tone peaks in the same band from the microphone and from 44.1 kHz speech: {Peak(micBands)}/{Peak(ttsBands)}");
        }
        Assert(low < mid && mid < high && high > VoiceSpectrum.Bands * 3 / 4, "FFT bands place a 150 Hz, 1 kHz and 5 kHz tone from low to high: " + low + "/" + mid + "/" + high);
        Assert(MicrophoneRecorder.Devices() is not null, "microphone enumeration does not throw");

        var window = new VoiceEqualizerWindow(); window.SetMode(true);
        try {
            window.Show(VoiceStage.Listening, "듣고 있어요"); window.SetSpectrum(Enumerable.Range(0, VoiceSpectrum.Bands).Select(b => b < 20 ? .8 : .3).ToArray()); Pump(600);
            Assert(window.IsVisible && window.Stage == VoiceStage.Listening && !window.IsActive, "equalizer shows above the taskbar without taking focus");
            Capture(window, Path.Combine(qa, "voice-listening.png"));
            Assert(window.Shape == DotShape.Equalizer, "listening spreads the dot line into an equalizer");
            window.Show(VoiceStage.Thinking, "생각하는 중"); Pump(900);
            Assert(window.Shape == DotShape.Sphere, "waiting for the answer gathers the dots into a rotating sphere");
            Capture(window, Path.Combine(qa, "voice-thinking.png"));
            window.Show(VoiceStage.Speaking, "읽는 중"); window.SetSpectrum(null); Pump(900);
            Capture(window, Path.Combine(qa, "voice-idle-line.png"));
            Assert(window.Shape == DotShape.Equalizer, "the spoken answer uses the same equalizer");
            bool clicked = false; window.ResultClicked += () => clicked = true;
            window.Show(VoiceStage.Result, "내일 10시에 회의가 있어요."); Pump(100);
            Capture(window, Path.Combine(qa, "voice-result.png"));
            var card = (System.Windows.Controls.Border)((System.Windows.Controls.StackPanel)window.Content).Children[0];
            card.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
            Assert(clicked, "clicking the result card asks to open the chat tab");
        } finally { window.Close(); }
    }

    // Live model check (--supertonic-live): synthesizes a Korean summary with local Supertonic and saves it for listening.
    private static void CheckSupertonicLive(string qa)
    {
        Assert(SupertonicSpeech.Available(SupertonicSpeech.DefaultFolder), "Supertonic models are installed");
        var voices = SupertonicSpeech.Voices(SupertonicSpeech.DefaultFolder);
        Assert(voices.Take(10).SequenceEqual(new[] { "F1", "F2", "F3", "F4", "F5", "M1", "M2", "M3", "M4", "M5" }) && SupertonicSpeech.Label("M3") == "Supertonic 남성 3" && SupertonicSpeech.Label("narrator").EndsWith("narrator"), "voice list puts the ten presets first and labels added styles");
        Assert(SupertonicSpeech.Prepare("오늘, 맑음").StartsWith("<ko>") && SupertonicSpeech.Prepare("오늘").EndsWith(".</ko>"), "Korean text is tagged and ends with punctuation");
        const string text = "오늘 서울 날씨는 맑음이고 기온은 13도 초반으로 쌀쌀합니다. 습도가 32%로 건조한 편이니 외출할 때 겉옷을 챙기는 게 좋겠습니다.";
        foreach (bool gpu in new[] { false, true }) {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        using var tts = new SupertonicSpeech(SupertonicSpeech.DefaultFolder, gpu);
        double load = timer.Elapsed.TotalSeconds;
        foreach (string voice in new[] { "F1", "M1", "F1" }) {
            timer.Restart(); var samples = tts.Synthesize(text, voice); double took = timer.Elapsed.TotalSeconds, length = samples.Length / (double)tts.SampleRate;
            File.WriteAllBytes(Path.Combine(qa, $"supertonic-{voice}.wav"), VoiceSpeaker.Wav(samples, tts.SampleRate));
            Console.WriteLine($"Supertonic {(gpu ? "GPU" : "CPU")} {voice}: load {load:N2}s, synth {took:N2}s for {length:N1}s audio");
            Assert(length > 4 && samples.Max(Math.Abs) > .05, "Supertonic produces audible Korean speech: " + voice);
            var spectra = VoiceSpeaker.Spectra(VoiceSpeaker.Wav(samples, tts.SampleRate), out _).Where(f => f.Max() > 0).ToArray();
            double average = spectra.Average(f => f.Average()), peak = spectra.Average(f => f.Max());
            Console.WriteLine($"Spectrum {voice}: mean band {average:N2}, mean frame peak {peak:N2}");
            Assert(average is > .1 and < .7 && peak is > .4 and <= 1, "real speech fills the equalizer without saturating: " + voice);
        }
        }
    }

    private static void Capture(Window window, string file)
    {
        var root = (FrameworkElement)window.Content;
        var bmp = new RenderTargetBitmap(Math.Max(1, (int)root.ActualWidth), Math.Max(1, (int)root.ActualHeight), 96, 96, PixelFormats.Pbgra32); bmp.Render(root);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bmp)); using var output = File.Create(file); png.Save(output);
    }
}
