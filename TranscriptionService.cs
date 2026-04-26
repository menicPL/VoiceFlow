using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Cloud.Speech.V1;
using Google.Protobuf;
using NAudio.Wave;
using Serilog;

namespace VoiceFlowCS
{
    public class TranscriptionService
    {
        private SpeechClient? _client;
        private SpeechClient.StreamingRecognizeStream? _stream;
        private bool _isRunning;
        private CancellationTokenSource? _cts;
        private Task? _receiveTask;

        public event Action<string, bool>? TranscriptReceived;

        public async Task StartAsync(string credentialsPath, int sampleRate, string primaryLanguage = "pl-PL")
        {
            Log.Information("Starting STT session. Lang: {Lang}, Rate: {Rate}", primaryLanguage, sampleRate);
            Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", credentialsPath);
            _client = await SpeechClient.CreateAsync();

            _stream = _client.StreamingRecognize();

            // Setup alternatives
            var alternatives = new List<string> { "en-US", "pl-PL" };
            alternatives.Remove(primaryLanguage);

            // Initial config
            await _stream.WriteAsync(new StreamingRecognizeRequest
            {
                StreamingConfig = new StreamingRecognitionConfig
                {
                    Config = new RecognitionConfig
                    {
                        Encoding = RecognitionConfig.Types.AudioEncoding.Linear16,
                        SampleRateHertz = sampleRate,
                        LanguageCode = primaryLanguage,
                        AlternativeLanguageCodes = { alternatives },
                        EnableAutomaticPunctuation = true,
                        UseEnhanced = true,
                        Model = "latest_long",

                        // Boost English tech words that get misrecognized in Polish context
                        SpeechContexts =
                        {
                            new SpeechContext
                            {
                                Phrases =
                                {
                                    "Gemini", "ChatGPT", "GPT", "OpenAI", "Claude", "Anthropic",
                                    "API", "Python", "GitHub", "Docker", "Kubernetes", "React",
                                    "TypeScript", "JavaScript", "C#", "SQL", "NoSQL", "Redis",
                                    "AWS", "Azure", "Google Cloud", "Elasticsearch", "Symfony",
                                    "VoiceFlow", "AI", "ML", "LLM", "prompt", "token", "stream",
                                    "loopback", "WASAPI", "endpoint", "webhook", "backend", "frontend",
                                    "Scrum", "Sprint", "Kanban", "backlog", "story", "epic",
                                    "standup", "retrospective", "Jira", "confluence", "velocity",
                                    "agile", "devops", "CI/CD", "pull request", "merge", "deploy"
                                },
                                Boost = 15
                            }
                        }
                    },
                    InterimResults = true
                }
            });

            _isRunning = true;
            _cts = new CancellationTokenSource();
            _receiveTask = Task.Run(() => ReceiveResponses(_cts.Token));
        }

        private int _sendCounter = 0;
        public async Task SendAudioAsync(byte[] buffer, int length)
        {
            if (!_isRunning || _stream == null) return;

            try
            {
                await _stream.WriteAsync(new StreamingRecognizeRequest
                {
                    AudioContent = ByteString.CopyFrom(buffer, 0, length)
                });

                _sendCounter++;
                if (_sendCounter >= 100)
                {
                    Log.Debug("Sent 100 audio chunks to Google STT.");
                    _sendCounter = 0;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error sending audio to Google STT");
            }
        }

        private async Task ReceiveResponses(CancellationToken token)
        {
            Log.Debug("Started receiving STT responses.");
            try
            {
                var responseStream = _stream!.GetResponseStream();
                while (await responseStream.MoveNextAsync(token))
                {
                    var response = responseStream.Current;
                    Log.Debug("Received STT response from Google.");
                    foreach (var result in response.Results)
                    {
                        if (result.Alternatives.Count > 0)
                        {
                            var transcript = result.Alternatives[0].Transcript;
                            var isFinal = result.IsFinal;
                            Log.Information("Transcript: {Text} (Final: {IsFinal})", transcript, isFinal);
                            TranscriptReceived?.Invoke(transcript, isFinal);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Log.Debug("STT receive loop canceled.");
            }
            catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.Cancelled)
            {
                Log.Debug("STT gRPC call canceled.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in STT receive loop");
            }
            Log.Debug("Stopped receiving STT responses.");
        }

        public async Task StopAsync()
        {
            _isRunning = false;
            _cts?.Cancel();

            if (_stream != null)
            {
                try
                {
                    // Don't wait forever for WriteComplete
                    await Task.WhenAny(_stream.WriteCompleteAsync(), Task.Delay(1000));
                }
                catch { }
                _stream = null;
            }

            if (_receiveTask != null)
            {
                try
                {
                    // Don't wait forever for the receive task
                    await Task.WhenAny(_receiveTask, Task.Delay(1000));
                }
                catch { }
                _receiveTask = null;
            }
            
            _cts?.Dispose();
            _cts = null;
        }
    }
}
