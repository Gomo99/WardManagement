using Microsoft.Extensions.Options;
using System.Text.Json;

namespace WARDMANAGEMENTSYSTEM.Services
{
    public class ReCaptchaService
    {
        private readonly HttpClient _httpClient;
        private readonly RecaptchaOptions _options;
        public string SiteKey => _options.SiteKey;

        public ReCaptchaService(HttpClient httpClient, IOptions<RecaptchaOptions> options)
        {
            _httpClient = httpClient;
            _options = options.Value;
        }

        public async Task<bool> ValidateTokenAsync(string token)
        {
            var response = await _httpClient.PostAsync(
                "https://www.google.com/recaptcha/api/siteverify",
                new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("secret", _options.SecretKey),
                    new KeyValuePair<string, string>("response", token)
                }));

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("success").GetBoolean();
        }
    }
}