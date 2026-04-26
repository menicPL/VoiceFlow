using System;
using System.IO;
using System.Text.Json;
using Serilog;

namespace VoiceFlowCS
{
    public class SettingsService
    {
        private readonly string _settingsPath;
        private AppSettings _settings;

        public SettingsService()
        {
            _settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
            _settings = LoadSettings();
        }

        public AppSettings Settings => _settings;

        public AppSettings LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    string json = File.ReadAllText(_settingsPath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings != null)
                    {
                        Log.Information("Settings loaded from {Path}", _settingsPath);
                        return settings;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error loading settings");
            }

            Log.Information("Using default settings");
            return new AppSettings();
        }

        public void SaveSettings()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(_settings, options);
                File.WriteAllText(_settingsPath, json);
                Log.Information("Settings saved to {Path}", _settingsPath);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error saving settings");
            }
        }

        public void ResetSettings()
        {
            _settings = new AppSettings();
            SaveSettings();
        }
    }
}
