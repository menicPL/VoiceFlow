using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace VoiceFlowCS
{
    public class AiService
    {
        private readonly string _apiKey;
        private readonly string _modelName;
        private readonly HttpClient _httpClient;

        public AiService(string apiKey, string modelName = "gemini-3.1-flash-lite-preview")
        {
            _apiKey = apiKey;
            _modelName = modelName;
            _httpClient = new HttpClient();
        }

        public async System.Collections.Generic.IAsyncEnumerable<string> GetResponseStreamAsync(string userText)
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_modelName}:streamGenerateContent?alt=sse&key={_apiKey}";
            Log.Debug("AI Stream URL: {Url}", url.Replace(_apiKey, "HIDDEN"));
            Log.Information("AI Query: {Query}", userText);

            var payload = new
            {
                contents = new[]
                {
                    new 
                    { 
                        parts = new[] 
                        { 
                            new { text = $"Zasady: Odpowiadaj krótko i po polsku. Pytanie: {userText}" } 
                        } 
                    }
                }
            };

            var json = JsonConvert.SerializeObject(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                Log.Error("AI Stream request failed: {Status} - {Error}", response.StatusCode, err);
                throw new Exception($"Gemini API Error: {response.StatusCode}");
            }

            using (var stream = await response.Content.ReadAsStreamAsync())
            using (var reader = new StreamReader(stream))
            {
                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync();
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (line.StartsWith("data: "))
                    {
                        var dataJson = line.Substring(6);
                        var data = JObject.Parse(dataJson);
                        var text = data["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString();
                        if (!string.IsNullOrEmpty(text))
                        {
                            yield return text;
                        }
                    }
                }
            }
        }
    }
}
