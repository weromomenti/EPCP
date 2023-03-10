using NAudio.CoreAudioApi;
using NAudio.Wave;
using Accord.Audio;
using Accord.Math;
using MathNet.Numerics.IntegralTransforms;
using MathNet.Numerics;
using Accord.Math.Kinematics;
using System.Runtime.Intrinsics.X86;
using MathNet.Numerics.Statistics;
using FftSharp;

namespace AudioMonitor;

public partial class FftMonitorForm : Form
{
    readonly double[] AudioValues;

    readonly WasapiCapture AudioDevice;
    readonly double[] FftValues;

    Dictionary<double, string> NoteFreqs;
    Dictionary<double, double> noteFreqs = new();

    public FftMonitorForm(WasapiCapture audioDevice)
    {
        InitializeComponent();
        AudioDevice = audioDevice;
        WaveFormat fmt = audioDevice.WaveFormat;

        AudioValues = new double[fmt.SampleRate / 10];
        double[] paddedAudio = Pad.ZeroPad(AudioValues);
        double[] fftMag = Transform.FFTpower(paddedAudio);
        FftValues = new double[fftMag.Length];
        double fftPeriod = Transform.FFTfreqPeriod(fmt.SampleRate, fftMag.Length);

        NoteFreqs = new Dictionary<double, string>() {
            { 32.703, "C" },
            { 34.648, "C#" },
            { 36.708, "D" },
            { 38.891, "D#" },
            { 41.203, "E" },
            { 43.654, "F" },
            { 46.249, "F#" },
            { 48.999, "G" },
            { 51.913, "G#" },
            { 55, "A" },
            { 58.270, "A#" },
            { 61.735, "B" } };

        double freq = 6.875;

        while (freq < 5250)
        {
            noteFreqs.Add(freq * Math.Pow(2, 1.0 / 12.0), 0);
            freq *= Math.Pow(2, 1.0 / 12.0);
        }

        formsPlot1.Plot.AddSignal(FftValues, 1.0 / fftPeriod);
        formsPlot1.Plot.YLabel("Spectral Power");
        formsPlot1.Plot.XLabel("Frequency (kHz)");
        formsPlot1.Plot.Title($"{fmt.Encoding} ({fmt.BitsPerSample}-bit) {fmt.SampleRate} KHz");
        formsPlot1.Plot.SetAxisLimits(0, 6000, 0, .00000005); // .00000005 // .0000000005 // .000000009
        formsPlot1.Refresh();

        AudioDevice.DataAvailable += WaveIn_DataAvailable;
        AudioDevice.StartRecording();

        FormClosed += FftMonitorForm_FormClosed;

        timer1.Interval = 1000;
    }

    private void FftMonitorForm_FormClosed(object? sender, FormClosedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"Closing audio device: {AudioDevice}");
        AudioDevice.StopRecording();
        AudioDevice.Dispose();
    }

    private void WaveIn_DataAvailable(object? sender, WaveInEventArgs e)
    {
        int bytesPerSamplePerChannel = AudioDevice.WaveFormat.BitsPerSample / 8;
        int bytesPerSample = bytesPerSamplePerChannel * AudioDevice.WaveFormat.Channels;
        int bufferSampleCount = e.Buffer.Length / bytesPerSample;

        if (bufferSampleCount >= AudioValues.Length)
        {
            bufferSampleCount = AudioValues.Length;
        }

        if (bytesPerSamplePerChannel == 2 && AudioDevice.WaveFormat.Encoding == WaveFormatEncoding.Pcm)
        {
            for (int i = 0; i < bufferSampleCount; i++)
                AudioValues[i] = BitConverter.ToInt16(e.Buffer, i * bytesPerSample);
        }
        else if (bytesPerSamplePerChannel == 4 && AudioDevice.WaveFormat.Encoding == WaveFormatEncoding.Pcm)
        {
            for (int i = 0; i < bufferSampleCount; i++)
                AudioValues[i] = BitConverter.ToInt32(e.Buffer, i * bytesPerSample);
        }
        else if (bytesPerSamplePerChannel == 4 && AudioDevice.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat)
        {
            for (int i = 0; i < bufferSampleCount; i++)
                AudioValues[i] = BitConverter.ToSingle(e.Buffer, i * bytesPerSample);
        }
        else
        {
            throw new NotSupportedException(AudioDevice.WaveFormat.ToString());
        }
    }

    private void timer1_Tick(object sender, EventArgs e)
    {
        double[] paddedAudio = Pad.ZeroPad(AudioValues); // there should be audio values

        FftSharp.Windows.Hamming hamming = new();
        hamming.ApplyInPlace(paddedAudio);

        double[] fftMag = Transform.FFTmagnitude(paddedAudio);

        double[] hps = new double[fftMag.Length];
        Array.Copy(fftMag, hps, hps.Length);

        double[] newFunc;
        for (int m = 1; m < 4; m++)
        {
            newFunc = ReducePeriod(fftMag, (int)Math.Pow(2, m));

            for (int i = 0; i < newFunc.Length; i++)
            {
                hps[i] *= newFunc[i];
            }
        }

        Array.Copy(hps, FftValues, hps.Length);

        double hpsPeriod = Transform.FFTfreqPeriod(AudioDevice.WaveFormat.SampleRate, hps.Length);
        double peakFrequency;

        double close, mag, freq;

        for (int i = 0; i < hps.Length; i++)
        {
            freq = i * hpsPeriod;
            mag = hps[i];
            close = noteFreqs.OrderBy(x => Math.Abs(x.Key - freq)).First().Key;
            noteFreqs[close] += mag;
        }
        double maxFreq = noteFreqs.Max(x => x.Value);

        double[] noteIntensity = new double[12];

        for (int i = 0; i < 12; i++)
        {
            noteIntensity[i] = noteFreqs.Where((item, index) => index % (i + 1) == 0).Sum(x => x.Value);
        }

        peakFrequency = noteFreqs.OrderByDescending(x => x.Value).First().Key;

        label1.Text = $"Peak Frequency: {peakFrequency:N0} Hz";

        noteFreqs = noteFreqs.ToDictionary(x => x.Key, x => 0.0);

        // request a redraw using a non-blocking, non-binary, queer render queue

        formsPlot1.RefreshRequest();
    }
    public static double[] ReducePeriod(double[] source, int period)
    {
        double[] result = new double[source.Length];

        for (int i = 0; i < source.Length / period; i++)
        {
            result[i] = source[i * period];
        }
        for (int i = source.Length / period; i < source.Length; i++)
        {
            result[i] = 0;
        }
        return result;
    }

    private void button1_Click(object sender, EventArgs e)
    {
        timer1.Enabled = timer1.Enabled == false ? timer1.Enabled = true : timer1.Enabled = false;
    }
}
