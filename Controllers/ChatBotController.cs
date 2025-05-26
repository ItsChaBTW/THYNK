using Microsoft.AspNetCore.Mvc;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace THYNK.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ChatBotController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private const string API_VERSION = "v1";
        private const string MODEL_NAME = "models/gemini-2.0-flash";

        public class ChatRequest
        {
            public string Message { get; set; }
        }

        public class GeminiRequest
        {
            public List<Content> contents { get; set; }
        }

        public class Content
        {
            public string role { get; set; }
            public List<Part> parts { get; set; }
        }

        public class Part
        {
            public string text { get; set; }
        }

        public ChatBotController(IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
        }

        [HttpPost("ask")]
        public async Task<IActionResult> Ask([FromBody] ChatRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request?.Message))
                {
                    return BadRequest("Message cannot be empty");
                }

                var client = _httpClientFactory.CreateClient();
                var apiKey = _configuration["Gemini:ApiKey"];

                if (string.IsNullOrEmpty(apiKey))
                {
                    return StatusCode(500, new { error = "API key not configured" });
                }

                var geminiRequest = new GeminiRequest
                {
                    contents = new List<Content>
                    {
                        new Content
                        {
                            role = "user",
                            parts = new List<Part>
                            {
                                new Part 
                                { 
                                    text = $"You are THYNK Assistant, an AI focused on emergency response and community safety. Answer the following question: {request.Message}" 
                                }
                            }
                        }
                    }
                };

                var content = new StringContent(
                    JsonSerializer.Serialize(geminiRequest, new JsonSerializerOptions 
                    { 
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = true 
                    }),
                    Encoding.UTF8,
                    "application/json"
                );

                var apiUrl = $"https://generativelanguage.googleapis.com/{API_VERSION}/{MODEL_NAME}:generateContent?key={apiKey}";
                Console.WriteLine($"Requesting URL: {apiUrl}"); // Debug logging
                var response = await client.PostAsync(apiUrl, content);

                var responseContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"API Response: {responseContent}"); // Debug logging

                if (!response.IsSuccessStatusCode)
                {
                    return StatusCode((int)response.StatusCode, new { error = $"API Error: {responseContent}" });
                }

                try
                {
                    var jsonResponse = JsonSerializer.Deserialize<JsonDocument>(responseContent);
                    var text = jsonResponse.RootElement
                        .GetProperty("candidates")[0]
                        .GetProperty("content")
                        .GetProperty("parts")[0]
                        .GetProperty("text")
                        .GetString();

                    return Ok(new { response = text });
                }
                catch (Exception jsonEx)
                {
                    return StatusCode(500, new { error = $"Failed to parse API response: {jsonEx.Message}", details = responseContent });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"Internal error: {ex.Message}" });
            }
        }
    }
} 