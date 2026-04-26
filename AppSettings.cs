using System;
using System.Collections.Generic;

namespace VoiceFlowCS
{
    public class AppSettings
    {
        public string GoogleCredsPath { get; set; } = "google_creds.json";
        public string GeminiApiKey { get; set; } = "";
        public string DefaultLanguage { get; set; } = "pl-PL";
        public string ModelName { get; set; } = "gemini-3.1-flash-lite-preview";
        public string SelectedDeviceId { get; set; } = "";
        public bool MixMic { get; set; } = false;
        public string SelectedMicId { get; set; } = "";
        public double FontSize { get; set; } = 13;
        public bool Topmost { get; set; } = true;
        public List<ChatMessage> ChatHistory { get; set; } = new List<ChatMessage>();
    }

    public class ChatMessage
    {
        public string Role { get; set; } = ""; // "You", "Gemini", "System"
        public string Content { get; set; } = "";
        public string Color { get; set; } = "Gray";
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}
