using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NAudio.CoreAudioApi;
using Serilog;

namespace VoiceFlowCS
{
    public class AudioService : IDisposable
    {
        private WasapiCapture? _capture;
        private WasapiCapture? _micCapture;

        // Mixing state
        private Thread? _mixThread;
        private volatile bool _mixRunning = false;
        private readonly object _mixLock = new object();
        private readonly List<float> _captureFloats = new List<float>();
        private readonly List<float> _micFloats = new List<float>();
        private bool _isMixMode = false;

        // Single-source state
        private readonly object _singleLock = new object();
        private readonly List<byte> _singleBuffer = new List<byte>();

        public event EventHandler<WaveInEventArgs>? DataAvailable;
        public event EventHandler<float>? VolumeUpdated;

        public int GetSampleRate() => 16000;

        public List<AudioDevice> GetDevices()
        {
            var devices = new List<AudioDevice>();
            var enumerator = new MMDeviceEnumerator();

            // Loopback (render) devices
            foreach (var ep in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                devices.Add(new AudioDevice { Id = ep.ID, Name = $"[LOOPBACK] {ep.FriendlyName}", IsLoopback = true });

            // Microphone (capture) devices
            foreach (var ep in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                devices.Add(new AudioDevice { Id = ep.ID, Name = $"[MIC] {ep.FriendlyName}", IsLoopback = false });

            return devices;
        }

        public void StartCapture(string deviceId, bool isLoopback, string? secondaryDeviceId = null)
        {
            try
            {
                Log.Debug("[AudioService] StartCapture called. DeviceId={DeviceId}, Loopback={IsLoopback}, SecondaryId={Secondary}", 
                    deviceId, isLoopback, secondaryDeviceId ?? "none");

                var enumerator = new MMDeviceEnumerator();
                var device = enumerator.GetDevice(deviceId);
                Log.Information("[AudioService] Primary device: {Name} ({Format})", 
                    device.FriendlyName, isLoopback ? "loopback" : "capture");

                if (isLoopback) _capture = new WasapiLoopbackCapture(device);
                else _capture = new WasapiCapture(device, true, 50);

                Log.Information("[AudioService] Primary WaveFormat: {Enc} {Bits}bit {Ch}ch {Rate}Hz",
                    _capture.WaveFormat.Encoding, _capture.WaveFormat.BitsPerSample,
                    _capture.WaveFormat.Channels, _capture.WaveFormat.SampleRate);

                _isMixMode = !string.IsNullOrEmpty(secondaryDeviceId);

                if (_isMixMode)
                {
                    // MIX MODE: thread-based mixer at 100ms intervals
                    var micDevice = enumerator.GetDevice(secondaryDeviceId!);
                    Log.Information("[AudioService] Secondary (mic) device: {Name}", micDevice.FriendlyName);
                    _micCapture = new WasapiCapture(micDevice, true, 50);
                    Log.Information("[AudioService] Mic WaveFormat: {Enc} {Bits}bit {Ch}ch {Rate}Hz",
                        _micCapture.WaveFormat.Encoding, _micCapture.WaveFormat.BitsPerSample,
                        _micCapture.WaveFormat.Channels, _micCapture.WaveFormat.SampleRate);

                    _capture.DataAvailable += OnCaptureData;
                    _micCapture.DataAvailable += OnMicData;

                    _mixRunning = true;
                    _mixThread = new Thread(MixerThread) { IsBackground = true, Name = "AudioMixThread" };
                    _mixThread.Start();

                    _micCapture.StartRecording();
                    _capture.StartRecording();
                    Log.Information("[AudioService] Mix mode started. MixThread running.");
                }
                else
                {
                    // SINGLE MODE: direct fast path
                    _capture.DataAvailable += OnSingleCaptureData;
                    _capture.StartRecording();
                    Log.Information("[AudioService] Single mode started.");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[AudioService] StartCapture failed");
                throw;
            }
        }

        // ── SINGLE SOURCE MODE ─────────────────────────────────────────────

        private void OnSingleCaptureData(object? sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded == 0) return;

            var fmt = _capture!.WaveFormat;
            var pcm = ConvertToPcm16k(e.Buffer, e.BytesRecorded, fmt, out float volume);

            byte[]? toEmit = null;
            lock (_singleLock)
            {
                if (pcm != null) _singleBuffer.AddRange(pcm);

                // emit every ~100ms (3200 bytes = 1600 samples * 2 bytes @ 16kHz mono)
                if (_singleBuffer.Count >= 3200)
                {
                    toEmit = _singleBuffer.ToArray();
                    _singleBuffer.Clear();
                }
            }

            if (toEmit != null)
            {
                Log.Debug("[AudioService] Single: emitting {Bytes}B, vol={Vol:F1}%", toEmit.Length, volume);
                DataAvailable?.Invoke(this, new WaveInEventArgs(toEmit, toEmit.Length));
            }
            VolumeUpdated?.Invoke(this, volume);
        }

        // ── MIX MODE ───────────────────────────────────────────────────────

        private void OnCaptureData(object? sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded == 0) return;
            var floats = ExtractFloatSamples(e.Buffer, e.BytesRecorded, _capture!.WaveFormat);
            lock (_mixLock)
            {
                _captureFloats.AddRange(floats);
            }
        }

        private void OnMicData(object? sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded == 0) return;
            var floats = ExtractFloatSamples(e.Buffer, e.BytesRecorded, _micCapture!.WaveFormat);
            lock (_mixLock)
            {
                _micFloats.AddRange(floats);
            }
        }

        /// <summary>
        /// Dedicated thread that runs every 100ms, mixes both buffers and emits one chunk.
        /// </summary>
        private void MixerThread()
        {
            Log.Debug("[AudioService] MixerThread started.");
            while (_mixRunning)
            {
                Thread.Sleep(100);

                float[] captureChunk;
                float[] micChunk;

                lock (_mixLock)
                {
                    captureChunk = _captureFloats.ToArray();
                    _captureFloats.Clear();
                    micChunk = _micFloats.ToArray();
                    _micFloats.Clear();
                }

                if (captureChunk.Length == 0 && micChunk.Length == 0) continue;

                // Downmix both to 16kHz mono and mix
                var capturePcm = ResampleAndDownmix(captureChunk, _capture?.WaveFormat);
                var micPcm = ResampleAndDownmix(micChunk, _micCapture?.WaveFormat);
                var mixed = MixPcm(capturePcm, micPcm);

                if (mixed.Length == 0) continue;

                double sumSq = 0;
                for (int i = 0; i < mixed.Length; i++) sumSq += (double)mixed[i] * mixed[i];
                float vol = (float)(Math.Sqrt(sumSq / mixed.Length) / 32768.0 * 100 * 5);

                Log.Debug("[AudioService] Mix: cap={CapSamples} mic={MicSamples} out={OutBytes}B vol={Vol:F1}%",
                    captureChunk.Length, micChunk.Length, mixed.Length * 2, vol);

                var pcmBytes = new byte[mixed.Length * 2];
                for (int i = 0; i < mixed.Length; i++)
                {
                    int sv = mixed[i]; // short → int, unambiguous
                    short s = sv > 32767 ? (short)32767 : sv < -32768 ? (short)-32768 : (short)sv;
                    pcmBytes[i * 2] = (byte)(s & 0xFF);
                    pcmBytes[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
                }

                DataAvailable?.Invoke(this, new WaveInEventArgs(pcmBytes, pcmBytes.Length));
                VolumeUpdated?.Invoke(this, vol);
            }
            Log.Debug("[AudioService] MixerThread stopped.");
        }

        // ── HELPERS ────────────────────────────────────────────────────────

        /// <summary>Extract mono float samples from raw buffer, handles 16/32-bit formats.</summary>
        private static float[] ExtractFloatSamples(byte[] buffer, int bytesRecorded, WaveFormat fmt)
        {
            int channels = fmt.Channels;
            int bitsPerSample = fmt.BitsPerSample;
            int bytesPerSample = bitsPerSample / 8;
            if (bytesPerSample == 0) return Array.Empty<float>();

            int frames = bytesRecorded / (bytesPerSample * channels);
            var result = new float[frames];

            for (int i = 0; i < frames; i++)
            {
                float mono = 0;
                for (int ch = 0; ch < channels; ch++)
                {
                    int offset = (i * channels + ch) * bytesPerSample;
                    if (offset + bytesPerSample > bytesRecorded) break;
                    float s = bitsPerSample == 32
                        ? BitConverter.ToSingle(buffer, offset)
                        : BitConverter.ToInt16(buffer, offset) / 32768f;
                    mono += s;
                }
                result[i] = mono / channels;
            }
            return result;
        }

        /// <summary>Downsample float array from source rate → 16kHz by averaging blocks.</summary>
        private static short[] ResampleAndDownmix(float[] samples, WaveFormat? fmt)
        {
            if (samples.Length == 0 || fmt == null) return Array.Empty<short>();

            int srcRate = fmt.SampleRate;
            int dstRate = 16000;

            if (srcRate == dstRate)
            {
                var r = new short[samples.Length];
                for (int i = 0; i < samples.Length; i++)
                    r[i] = (short)Math.Clamp(samples[i] * 32767, -32768, 32767);
                return r;
            }

            // Simple block-averaging downsample
            double ratio = (double)srcRate / dstRate;
            int outLen = (int)(samples.Length / ratio);
            var result = new short[outLen];

            for (int i = 0; i < outLen; i++)
            {
                int startSrc = (int)(i * ratio);
                int endSrc = Math.Min((int)((i + 1) * ratio), samples.Length);
                float sum = 0;
                for (int j = startSrc; j < endSrc; j++) sum += samples[j];
                float avg = endSrc > startSrc ? sum / (endSrc - startSrc) : 0;
                result[i] = (short)Math.Clamp(avg * 32767, -32768, 32767);
            }
            return result;
        }

        /// <summary>Mix two PCM streams by clamped addition. Pads shorter with silence.</summary>
        private static short[] MixPcm(short[] a, short[] b)
        {
            int len = Math.Max(a.Length, b.Length);
            if (len == 0) return Array.Empty<short>();
            var result = new short[len];
            for (int i = 0; i < len; i++)
            {
                int va = i < a.Length ? (int)a[i] : 0;
                int vb = i < b.Length ? (int)b[i] : 0;
                result[i] = (short)Math.Clamp(va + vb, (int)-32768, (int)32767);
            }
            return result;
        }

        /// <summary>Convert a raw WASAPI buffer to 16kHz PCM bytes (single source path).</summary>
        private static byte[]? ConvertToPcm16k(byte[] buffer, int bytesRecorded, WaveFormat fmt, out float volume)
        {
            volume = 0;
            var floats = ExtractFloatSamples(buffer, bytesRecorded, fmt);
            if (floats.Length == 0) return null;

            var pcm = ResampleAndDownmix(floats, fmt);
            if (pcm.Length == 0) return null;

            double sumSq = 0;
            for (int i = 0; i < pcm.Length; i++) sumSq += (double)pcm[i] * pcm[i];
            volume = (float)(Math.Sqrt(sumSq / pcm.Length) / 32768.0 * 100 * 5);

            var bytes = new byte[pcm.Length * 2];
            for (int i = 0; i < pcm.Length; i++)
            {
                bytes[i * 2] = (byte)(pcm[i] & 0xFF);
                bytes[i * 2 + 1] = (byte)((pcm[i] >> 8) & 0xFF);
            }
            return bytes;
        }

        // ── CLEANUP ────────────────────────────────────────────────────────

        public void StopCapture()
        {
            Log.Debug("[AudioService] StopCapture called.");

            _mixRunning = false;

            _capture?.StopRecording();
            _capture?.Dispose();
            _capture = null;

            _micCapture?.StopRecording();
            _micCapture?.Dispose();
            _micCapture = null;

            _mixThread?.Join(2000);
            _mixThread = null;

            lock (_mixLock) { _captureFloats.Clear(); _micFloats.Clear(); }
            lock (_singleLock) { _singleBuffer.Clear(); }

            Log.Debug("[AudioService] StopCapture complete.");
        }

        public void Dispose() => StopCapture();
    }

    public class AudioDevice
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public bool IsLoopback { get; set; }
    }
}
