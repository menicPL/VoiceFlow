using System;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Input;
using DotNetEnv;
using Serilog;
using System.IO;
using System.Text;
using NAudio.Wave;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace VoiceFlowCS
{
    public partial class MainWindow : Window
    {
        private readonly AudioService _audioService;
        private readonly TranscriptionService _transcriptionService;
        private AiService _aiService;
        private readonly SettingsService _settingsService;
        private bool _isActive;

        private readonly StringBuilder _sessionText = new StringBuilder();

        public MainWindow()
        {
            InitializeComponent();
            
            // Advanced Logging Setup
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console()
                .WriteTo.File("logs/voiceflow.log", rollingInterval: RollingInterval.Day)
                .CreateLogger();

            Log.Information("VoiceFlow CS starting up...");
            // Load environment variables from .env located in the executable's directory
            var envPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".env");
            if (System.IO.File.Exists(envPath))
            {
                Env.Load(envPath);
                Log.Information("Loaded .env from {Path}", envPath);
            }
            else
            {
                Log.Warning(".env file not found at {Path}. Environment variables may be missing.", envPath);
            }
            
            _settingsService = new SettingsService();
            _audioService = new AudioService();
            _transcriptionService = new TranscriptionService();

            var apiKey = _settingsService.Settings.GeminiApiKey;
            if (string.IsNullOrEmpty(apiKey))
            {
                apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
            }

            if (string.IsNullOrEmpty(apiKey))
            {
                AppendText("Warning: GEMINI_API_KEY not found. AI features will be disabled.", Brushes.Orange, false);
                _aiService = null!;
            }
            else
            {
                _aiService = new AiService(apiKey, _settingsService.Settings.ModelName);
            }

            LoadDevices();
            ApplySettings();
            LoadChatHistory();
            
            _audioService.VolumeUpdated += (s, vol) => Dispatcher.BeginInvoke(new Action(() => VolumeBar.Value = vol));
            _transcriptionService.TranscriptReceived += OnTranscriptReceived;
        }

        private void ApplySettings()
        {
            var settings = _settingsService.Settings;
            
            // Language
            foreach (ComboBoxItem item in LanguageComboBox.Items)
            {
                if (item.Tag?.ToString() == settings.DefaultLanguage)
                {
                    LanguageComboBox.SelectedItem = item;
                    break;
                }
            }

            // Mix Mic
            MixMicCheckBox.IsChecked = settings.MixMic;
            MicComboBox.IsEnabled = settings.MixMic;

            // UI Preferences
            TranscriptBox.FontSize = settings.FontSize;
            this.Topmost = settings.Topmost;
            SettingsFontSize.Value = settings.FontSize;
            SettingsTopmost.IsChecked = settings.Topmost;
            SettingsGooglePath.Text = settings.GoogleCredsPath;
            SettingsGeminiKey.Password = settings.GeminiApiKey;

            // Model selection
            foreach (ComboBoxItem item in SettingsModel.Items)
            {
                if (item.Content?.ToString() == settings.ModelName)
                {
                    SettingsModel.SelectedItem = item;
                    break;
                }
            }
        }

        private void LoadChatHistory()
        {
            foreach (var msg in _settingsService.Settings.ChatHistory)
            {
                var brush = (Brush)new BrushConverter().ConvertFromString(msg.Color) ?? Brushes.Gray;
                AppendText($"{msg.Role}: {msg.Content}", brush, false);
            }
        }

        private void LoadDevices()
        {
            var devices = _audioService.GetDevices();
            DeviceComboBox.ItemsSource = devices;
            DeviceComboBox.DisplayMemberPath = "Name";

            // Select the device from settings or 3rd loopback default
            var savedDeviceId = _settingsService.Settings.SelectedDeviceId;
            var savedMicId = _settingsService.Settings.SelectedMicId;

            if (!string.IsNullOrEmpty(savedDeviceId))
            {
                var device = devices.FirstOrDefault(d => d.Id == savedDeviceId);
                if (device != null) DeviceComboBox.SelectedItem = device;
            }

            if (DeviceComboBox.SelectedItem == null)
            {
                var loopbackDevices = devices.Where(d => d.IsLoopback).ToList();
                var defaultDevice = loopbackDevices.Count >= 3 ? loopbackDevices[2] : loopbackDevices.FirstOrDefault();
                DeviceComboBox.SelectedItem = defaultDevice ?? (devices.Count > 0 ? devices[0] : null);
            }

            // Separate list for Mic selection
            var mics = devices.Where(d => !d.IsLoopback).ToList();
            MicComboBox.ItemsSource = mics;
            MicComboBox.DisplayMemberPath = "Name";

            if (!string.IsNullOrEmpty(savedMicId))
            {
                var mic = mics.FirstOrDefault(d => d.Id == savedMicId);
                if (mic != null) MicComboBox.SelectedItem = mic;
            }
            
            if (MicComboBox.SelectedItem == null && MicComboBox.Items.Count > 0) 
                MicComboBox.SelectedIndex = 0;
        }

        private void MixMicCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (MicComboBox != null)
                MicComboBox.IsEnabled = MixMicCheckBox.IsChecked == true;
        }

        private void Window_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Window_Closing(this, new System.ComponentModel.CancelEventArgs());
            Close();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
                MaximizeBtn.Content = "▢";
            }
            else
            {
                WindowState = WindowState.Maximized;
                MaximizeBtn.Content = "❐";
            }
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            SettingsGeminiKey.Password = _settingsService.Settings.GeminiApiKey;
            SettingsGooglePath.Text = _settingsService.Settings.GoogleCredsPath;
            SettingsFontSize.Value = _settingsService.Settings.FontSize;
            SettingsTopmost.IsChecked = _settingsService.Settings.Topmost;
            
            SettingsOverlay.Visibility = Visibility.Visible;
            var sb = (System.Windows.Media.Animation.Storyboard)FindResource("ShowSettings");
            sb.Begin();
        }

        private async void SettingsCancel_Click(object sender, RoutedEventArgs e)
        {
            var sb = (System.Windows.Media.Animation.Storyboard)FindResource("HideSettings");
            sb.Begin();
            await Task.Delay(200);
            SettingsOverlay.Visibility = Visibility.Collapsed;
        }

        private async void SettingsSave_Click(object sender, RoutedEventArgs e)
        {
            var settings = _settingsService.Settings;
            settings.GeminiApiKey = SettingsGeminiKey.Password;
            settings.GoogleCredsPath = SettingsGooglePath.Text;
            settings.ModelName = (SettingsModel.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "gemini-2.0-flash";
            settings.FontSize = SettingsFontSize.Value;
            settings.Topmost = SettingsTopmost.IsChecked == true;

            _settingsService.SaveSettings();
            ApplySettings();
            
            var sb = (System.Windows.Media.Animation.Storyboard)FindResource("HideSettings");
            sb.Begin();
            await Task.Delay(200);
            SettingsOverlay.Visibility = Visibility.Collapsed;
            
            // Re-initialize AI Service
            if (!string.IsNullOrEmpty(settings.GeminiApiKey))
            {
                _aiService = new AiService(settings.GeminiApiKey, settings.ModelName);
            }
        }

        private bool _isProcessing;
        private async void ToggleSession_Click(object sender, RoutedEventArgs e)
        {
            if (_isProcessing) return;
            _isProcessing = true;
            try
            {
                Log.Information("ToggleSession_Click called. Current status: {Status}", _isActive ? "Active" : "Inactive");
                if (!_isActive)
                {
                    var selectedDevice = (AudioDevice)DeviceComboBox.SelectedItem;
                    if (selectedDevice == null) 
                    {
                        Log.Warning("No device selected in ComboBox!");
                        return;
                    }

                    Log.Information("Starting session with device: {Name} (ID: {Id})", selectedDevice.Name, selectedDevice.Id);

                    string creds = _settingsService.Settings.GoogleCredsPath;
                    if (string.IsNullOrEmpty(creds)) creds = "google_creds.json";
                    if (!File.Exists(creds)) creds = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, creds);

                    if (!File.Exists(creds))
                    {
                        Log.Error("Credentials file not found at {Path}", Path.GetFullPath(creds));
                        AppendText($"Error: {creds} not found!", Brushes.Red, false);
                        return;
                    }

                    Log.Information("Initializing audio and transcription services...");
                    AppendText($"--- Switched to: {selectedDevice.Name} ---", Brushes.Green);
                    
                    _audioService.DataAvailable -= OnAudioDataAvailable;
                    _audioService.DataAvailable += OnAudioDataAvailable;

                    string? secondaryId = null;
                    if (MixMicCheckBox.IsChecked == true && MicComboBox.SelectedItem is AudioDevice mic)
                    {
                        secondaryId = mic.Id;
                        AppendText($"--- Mixed with: {mic.Name} ---", Brushes.Green);
                    }

                    Log.Information("Calling StartCapture...");
                    _audioService.StartCapture(selectedDevice.Id!, selectedDevice.IsLoopback, secondaryId);
                    
                    int sampleRate = 16000; 
                    Log.Information("Audio capture initialized at {Rate}Hz. Starting STT...", sampleRate);

                    string lang = (LanguageComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "pl-PL";
                    await _transcriptionService.StartAsync(creds, sampleRate, lang); 
                    Log.Information("Transcription service started.");

                    StatusLabel.Text = $"● Listening on {selectedDevice.Name}";
                    StartBtn.Content = "Stop Transcription";
                    StartBtn.Background = new LinearGradientBrush(Colors.Red, Color.FromRgb(185, 28, 28), 0);
                    _isActive = true;
                }
                else
                {
                    Log.Information("Stopping session...");
                    StatusLabel.Text = "Stopping...";
                    _audioService.DataAvailable -= OnAudioDataAvailable;
                    _audioService.StopCapture();
                    await _transcriptionService.StopAsync();
                    
                    // Auto-finalize interim text — show as blue, keep in session for next Get Answer
                    Dispatcher.Invoke(() =>
                    {
                        lock (_uiLock)
                        {
                            if (_interimRun != null && !string.IsNullOrEmpty(_interimRun.Text))
                            {
                                var leftoverText = _interimRun.Text.Replace("...", "").Trim();
                                MainParagraph.Inlines.Remove(_interimRun);
                                _interimRun = null;
                                if (!string.IsNullOrEmpty(leftoverText))
                                {
                                    AppendText($"You: {leftoverText}", Brushes.SkyBlue);
                                    _sessionText.AppendLine(leftoverText);
                                }
                            }
                        }
                    });
                    
                    StatusLabel.Text = "● Ready";
                    StartBtn.Content = "Start Transcription";
                    StartBtn.Background = new LinearGradientBrush(Color.FromRgb(37, 99, 235), Color.FromRgb(29, 78, 216), 0);
                    _isActive = false;
                    VolumeBar.Value = 0;
                    Log.Information("Session stopped.");
                }
                StartBtn.IsEnabled = true;
                GetAnswerBtn.IsEnabled = _isActive;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ToggleSession error");
                MessageBox.Show($"Error: {ex.Message}");
                _isActive = false;
                StartBtn.Content = "Start Transcription";
                StartBtn.IsEnabled = true;
            }
            finally
            {
                _isProcessing = false;
            }
        }



        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            MainParagraph.Inlines.Clear();
            _sessionText.Clear();
            _accumulated = "";
            _currentBest = "";
            _interimRun = null;
            _settingsService.Settings.ChatHistory.Clear();
            _settingsService.SaveSettings();
        }

        /// <summary>Clears only the session buffer (text queued for Gemini), not the chat history.</summary>
        private void ClearQuery_Click(object sender, RoutedEventArgs e)
        {
            lock (_uiLock)
            {
                _sessionText.Clear();
                _accumulated = "";
                _currentBest = "";
                if (_interimRun != null)
                {
                    MainParagraph.Inlines.Remove(_interimRun);
                    _interimRun = null;
                }
            }
            AppendText("🗑 Bufor wyczyszczony — słucham od nowa.", Brushes.DarkGray);
        }

        // New: handle manual query input
        private void SendManual_Click(object sender, RoutedEventArgs e)
        {
            var query = ManualInput.Text?.Trim();
            if (string.IsNullOrEmpty(query))
                return;

            // Show manual query in transcript
            AppendText($"✏️ Ty: {query}", Brushes.LightGoldenrodYellow);
            _sessionText.AppendLine(query);

            // Trigger Gemini with the manual query
            TriggerGemini(query);

            // Clear input field
            ManualInput.Clear();
        }

        // New: handle Enter key in ManualInput (multi-line behavior)
        private void ManualInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
            {
                SendManual_Click(sender, new RoutedEventArgs());
                e.Handled = true;
            }
            // Shift+Enter → natural newline (AcceptsReturn="True")
        }

        private void ManualInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            // Auto-scroll chat to bottom when user types in the input field
            ChatScroll.ScrollToEnd();
        }

        // New: Get Answer button handler (does not stop transcription)
        private void GetAnswer_Click(object sender, RoutedEventArgs e)
        {
            if (_aiService == null)
            {
                AppendText("⚠️ GEMINI_API_KEY not set – AI features are disabled.", Brushes.Orange);
                return;
            }

            // Build query: finalized sentences (with newlines) + any in-progress speech (_currentBest)
            // NOTE: _interimRun.Text = _accumulated (same as _sessionText) — do NOT use it to avoid duplication
            var query = _sessionText.ToString().Trim();
            if (!string.IsNullOrEmpty(_currentBest))
                query = string.IsNullOrEmpty(query) ? _currentBest : query + " " + _currentBest;

            // Remove gray run from UI
            lock (_uiLock)
            {
                if (_interimRun != null)
                {
                    MainParagraph.Inlines.Remove(_interimRun);
                    _interimRun = null;
                }
            }

            if (string.IsNullOrEmpty(query))
                return;

            // Reset all buffers — next listen round starts fresh
            _sessionText.Clear();
            _accumulated = "";
            _currentBest = "";

            AppendText($"📝 Zapytanie:\n{query}", Brushes.Khaki);
            TriggerGemini(query);
        }

        // New: graceful shutdown on window closing
        private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                // Save current selections
                _settingsService.Settings.SelectedDeviceId = (DeviceComboBox.SelectedItem as AudioDevice)?.Id ?? "";
                _settingsService.Settings.SelectedMicId = (MicComboBox.SelectedItem as AudioDevice)?.Id ?? "";
                _settingsService.Settings.DefaultLanguage = (LanguageComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "pl-PL";
                _settingsService.Settings.MixMic = MixMicCheckBox.IsChecked == true;
                _settingsService.SaveSettings();

                if (_isActive)
                {
                    // Stop audio capture and transcription services
                    _audioService.DataAvailable -= OnAudioDataAvailable;
                    _audioService.StopCapture();
                    await _transcriptionService.StopAsync();
                    _isActive = false;
                }
                _audioService.Dispose();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during application shutdown");
            }
        }

        private async void OnAudioDataAvailable(object? sender, WaveInEventArgs e)
        {
            await _transcriptionService.SendAudioAsync(e.Buffer, e.BytesRecorded);
        }

        private Run? _interimRun;
        private readonly object _uiLock = new object();
        private string _accumulated = "";   // finalized words (grows on every final)
        private string _currentBest = "";   // best interim for ongoing utterance (never shrinks)

        private void OnTranscriptReceived(string text, bool isFinal)
        {
            // Skip empty results (Google STT sometimes returns empty finals)
            if (string.IsNullOrWhiteSpace(text)) return;

            if (isFinal)
            {
                Log.Information("Final: {Text}", text);
                Console.WriteLine($"[YOU]: {text}");
                File.AppendAllText("transcripts.txt", $"[{DateTime.Now:HH:mm:ss}] You: {text}\n");
                _sessionText.AppendLine(text);

                // Finalize: grow accumulated, reset per-utterance best
                _accumulated = string.IsNullOrEmpty(_accumulated) ? text : _accumulated + " " + text;
                _currentBest = "";

                var snap = _accumulated;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    lock (_uiLock)
                    {
                        if (_interimRun == null)
                        {
                            _interimRun = new Run { Foreground = Brushes.Gray, FontStyle = FontStyles.Italic };
                            MainParagraph.Inlines.Add(_interimRun);
                        }
                        // Show finalized text (no "..." — utterance ended)
                        _interimRun.Text = "\n" + snap;
                        ChatScroll.ScrollToEnd();
                    }
                }));
            }
            else
            {
                // Interim: only advance if STT gives us MORE text than before (never shrink)
                if (text.Length > _currentBest.Length)
                    _currentBest = text;

                var display = string.IsNullOrEmpty(_accumulated)
                    ? _currentBest
                    : _accumulated + " " + _currentBest;

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    lock (_uiLock)
                    {
                        if (_interimRun == null)
                        {
                            _interimRun = new Run { Foreground = Brushes.Gray, FontStyle = FontStyles.Italic };
                            MainParagraph.Inlines.Add(_interimRun);
                        }
                        _interimRun.Text = "\n" + display + "...";
                        ChatScroll.ScrollToEnd();
                    }
                }));
            }
        }

        private void TriggerGemini(string text)
        {
            _ = Task.Run(async () => 
            {
                Run? currentAiRun = null;
                await Dispatcher.InvokeAsync(() =>
                {
                    AiStatusIndicator.Fill = Brushes.MediumPurple;
                    // Insert Gemini label before the interim run (keeps interim at bottom)
                    var labelRun = new Run("\n🤖 Gemini: ") { Foreground = Brushes.MediumPurple };
                    currentAiRun = new Run { Foreground = Brushes.MediumPurple };
                    if (_interimRun != null)
                    {
                        MainParagraph.Inlines.InsertBefore(_interimRun, labelRun);
                        MainParagraph.Inlines.InsertBefore(_interimRun, currentAiRun);
                    }
                    else
                    {
                        MainParagraph.Inlines.Add(labelRun);
                        MainParagraph.Inlines.Add(currentAiRun);
                    }
                    ChatScroll.UpdateLayout();
                    ChatScroll.ScrollToEnd();
                });

                try
                {
                    await foreach (var chunk in _aiService.GetResponseStreamAsync(text))
                    {
                        // InvokeAsync (not BeginInvoke) ensures we wait for render before scrolling
                        await Dispatcher.InvokeAsync(() =>
                        {
                            if (currentAiRun != null) currentAiRun.Text += chunk;
                            ChatScroll.UpdateLayout();
                            ChatScroll.ScrollToEnd();
                        });
                    }

                    // Final scroll after stream completes
                    await Dispatcher.InvokeAsync(() =>
                    {
                        AiStatusIndicator.Fill = Brushes.Gray;
                        ChatScroll.UpdateLayout();
                        ChatScroll.ScrollToEnd();
                    });
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "AI Stream error");
                    await Dispatcher.InvokeAsync(() =>
                    {
                        AiStatusIndicator.Fill = Brushes.Red;
                        if (currentAiRun != null) currentAiRun.Text += $"\n[Error: {ex.Message}]";
                        ChatScroll.UpdateLayout();
                        ChatScroll.ScrollToEnd();
                    });
                }
            });
        }

        private void AppendText(string text, Brush color, bool saveToHistory = true)
        {
            // Prepend a newline so every message starts on its own line
            var run = new Run("\n" + text) { Foreground = color };
            if (_interimRun != null)
            {
                MainParagraph.Inlines.InsertBefore(_interimRun, run);
            }
            else
            {
                MainParagraph.Inlines.Add(run);
            }
            ChatScroll.ScrollToEnd();

            if (saveToHistory)
            {
                string role = "System";
                string content = text;
                if (text.StartsWith("You: ")) { role = "You"; content = text.Substring(5); }
                else if (text.StartsWith("✏️ Ty: ")) { role = "You"; content = text.Substring(7); }
                else if (text.StartsWith("🤖 Gemini: ")) { role = "Gemini"; content = text.Substring(11); }
                else if (text.StartsWith("📝 Zapytanie:\n")) { role = "Query"; content = text.Substring(14); }

                _settingsService.Settings.ChatHistory.Add(new ChatMessage
                {
                    Role = role,
                    Content = content,
                    Color = color.ToString(),
                    Timestamp = DateTime.Now
                });
                // Keep history reasonable
                if (_settingsService.Settings.ChatHistory.Count > 100)
                    _settingsService.Settings.ChatHistory.RemoveAt(0);
            }
        }
    }
}
