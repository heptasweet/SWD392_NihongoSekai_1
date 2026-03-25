using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using System;
using System.Linq;
using System.Text.Json;
using System.Text;
using JapaneseLearningPlatform.Data.ViewModels;

namespace JapaneseLearningPlatform.Data.Services
{
    public class AIExplanationService : IAIExplanationService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;

        public AIExplanationService(HttpClient httpClient, IOptions<AIOptions> options)
        {
            _httpClient = httpClient;
            _apiKey = options.Value.GeminiApiKey;
            _httpClient.BaseAddress = new Uri("https://generativelanguage.googleapis.com/v1beta/");
        }

        public async Task<Dictionary<int, string>> GetExplanationsAsync(IEnumerable<AIExplanationRequest> requests)
        {
            var list = requests.ToList();
            if (!list.Any()) return new Dictionary<int, string>();

            // Build a single prompt that asks the model to explain each question separately and label by id
            var sb = new StringBuilder();
            sb.AppendLine("Bạn là một trợ lý giáo viên. Hãy cho lời giải thích ngắn gọn (1-2 câu, tối đa 80 từ) cho mỗi câu hỏi sau.");
            sb.AppendLine("QUAN TRỌNG: Chỉ trả về một JSON mảng duy nhất, không kèm văn bản giải thích khác. Mỗi phần tử phải có hai trường: 'QuestionId' (số) và 'Explanation' (chuỗi). Ví dụ:\n[{\"QuestionId\":123, \"Explanation\": \"Giải thích ngắn...\"}, {\"QuestionId\":124, \"Explanation\": \"...\"}]\n");

            foreach (var r in list)
            {
                var userAnswer = string.IsNullOrWhiteSpace(r.UserAnswer) ? "Không có đáp án" : r.UserAnswer;
                var correctAnswer = string.IsNullOrWhiteSpace(r.CorrectAnswer) ? "Không có đáp án đúng" : r.CorrectAnswer;
                sb.AppendLine($"QuestionId: {r.QuestionId}");
                sb.AppendLine($"Question: {r.QuestionText}");
                sb.AppendLine($"UserAnswer: {userAnswer}");
                sb.AppendLine($"CorrectAnswer: {correctAnswer}");
                sb.AppendLine();
            }

            var prompt = sb.ToString();

            var requestBody = new
            {
                contents = new[]
                {
                    new {
                        role = "user",
                        parts = new[] { new { text = prompt } }
                    }
                }
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"models/gemini-2.5-flash:generateContent?key={_apiKey}", content);

            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();

            if (!json.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
                return list.ToDictionary(l => l.QuestionId, l => "[AI Error]: No AI response.");

            var parts = candidates[0].GetProperty("content").GetProperty("parts");
            if (parts.GetArrayLength() == 0)
                return list.ToDictionary(l => l.QuestionId, l => "[AI Error]: No content parts.");

            var text = parts[0].GetProperty("text").GetString() ?? string.Empty;

            // Try to extract a JSON array from the model response and parse it.
            try
            {
                // Find first JSON array in the text using regex
                var arrayMatch = System.Text.RegularExpressions.Regex.Match(text, "\\[\\s*\\{.*?\\}\\s*\\]", System.Text.RegularExpressions.RegexOptions.Singleline);
                if (arrayMatch.Success)
                {
                    var arrText = arrayMatch.Value;

                    var jsonDoc = JsonDocument.Parse(arrText);
                    if (jsonDoc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        var dict = new Dictionary<int, string>();
                        foreach (var el in jsonDoc.RootElement.EnumerateArray())
                        {
                            int id = 0;
                            string expl = string.Empty;

                            if (el.TryGetProperty("QuestionId", out var idProp))
                            {
                                if (idProp.ValueKind == JsonValueKind.Number && idProp.TryGetInt32(out var nid)) id = nid;
                                else if (idProp.ValueKind == JsonValueKind.String && int.TryParse(idProp.GetString(), out var sid)) id = sid;
                            }

                            if (el.TryGetProperty("Explanation", out var explProp))
                            {
                                if (explProp.ValueKind == JsonValueKind.String) expl = explProp.GetString() ?? string.Empty;
                            }

                            if (id != 0)
                                dict[id] = string.IsNullOrWhiteSpace(expl) ? "[AI Error]: Empty explanation." : expl;
                        }

                        // Fill missing with error
                        foreach (var r in list)
                            if (!dict.ContainsKey(r.QuestionId)) dict[r.QuestionId] = "[AI Error]: No explanation returned for this question.";

                        return dict;
                    }
                }
            }
            catch { /* ignore parse errors, fallback to heuristic */ }

            // Fallback heuristic: split by "QuestionId:" occurrences
            var results = new Dictionary<int, string>();
            foreach (var r in list) results[r.QuestionId] = "[AI Error]: Unable to parse AI response.";

            // Try to locate each question's explanation in the text
            foreach (var r in list)
            {
                var marker = $"QuestionId: {r.QuestionId}";
                var idx = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    // take substring until next marker or end
                    var start = idx + marker.Length;
                    var nextIdx = text.IndexOf("QuestionId:", start, StringComparison.OrdinalIgnoreCase);
                    string segment;
                    if (nextIdx > start)
                        segment = text.Substring(start, nextIdx - start);
                    else
                        segment = text.Substring(start);

                    var cleaned = segment.Trim();
                    results[r.QuestionId] = cleaned.Length > 0 ? cleaned : results[r.QuestionId];
                }
            }

            return results;
        }

        public async Task<string> GetExplanationAsync(string question, string userAnswer, string correctAnswer)
        {
            if (string.IsNullOrWhiteSpace(userAnswer)) userAnswer = "Không có đáp án";
            if (string.IsNullOrWhiteSpace(correctAnswer)) correctAnswer = "Không có đáp án đúng";

            var prompt = $"Giải thích tại sao câu trả lời đúng cho câu hỏi '{question}' là '{correctAnswer}' và vì sao người dùng trả lời '{userAnswer}' là sai. Giải thích trong một đoạn ngắn với khoảng 1-2 câu, tối đa là 80 từ và tham khảo các tài liệu liên quan để trả lời.";

            var requestBody = new
            {
                contents = new[]
                {
            new {
                role = "user",
                parts = new[] { new { text = prompt } }
            }
        }
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"models/gemini-2.5-flash:generateContent?key={_apiKey}", content);

            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();

            if (!json.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
                return "[AI Error]: No AI response.";

            var parts = candidates[0].GetProperty("content").GetProperty("parts");
            if (parts.GetArrayLength() == 0)
                return "[AI Error]: No content parts.";

            return parts[0].GetProperty("text").GetString();
        }
    }
}