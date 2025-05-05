using System.Net.Http.Headers;
using System.Text.Json;
using THYNK.Models;

namespace THYNK.Services
{
    public interface IPSGCService
    {
        Task<List<PSGCData>> GetRegions();
        Task<List<PSGCData>> GetProvinces(string regionCode);
        Task<List<PSGCData>> GetCityMunicipalities(string provinceCode);
        Task<List<PSGCData>> GetBarangays(string cityMunicipalityCode);
    }

    public class PSGCService : IPSGCService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<PSGCService> _logger;

        public PSGCService(HttpClient httpClient, ILogger<PSGCService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _httpClient.BaseAddress = new Uri("https://psgc.gitlab.io/api/");
            _httpClient.DefaultRequestHeaders.Accept.Clear();
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "THYNK-Application");
        }

        public async Task<List<PSGCData>> GetRegions()
        {
            try
            {
                var response = await _httpClient.GetAsync("regions");
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<List<PSGCData>>(content);
                return data ?? new List<PSGCData>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching regions from PSGC API");
                return new List<PSGCData>();
            }
        }

        public async Task<List<PSGCData>> GetProvinces(string regionCode)
        {
            try
            {
                var response = await _httpClient.GetAsync($"regions/{regionCode}/provinces");
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<List<PSGCData>>(content);
                return data ?? new List<PSGCData>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching provinces from PSGC API");
                return new List<PSGCData>();
            }
        }

        public async Task<List<PSGCData>> GetCityMunicipalities(string provinceCode)
        {
            try
            {
                var response = await _httpClient.GetAsync($"provinces/{provinceCode}/cities-municipalities");
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<List<PSGCData>>(content);
                return data ?? new List<PSGCData>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching cities/municipalities from PSGC API");
                return new List<PSGCData>();
            }
        }

        public async Task<List<PSGCData>> GetBarangays(string cityMunicipalityCode)
        {
            try
            {
                var response = await _httpClient.GetAsync($"cities-municipalities/{cityMunicipalityCode}/barangays");
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<List<PSGCData>>(content);
                return data ?? new List<PSGCData>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching barangays from PSGC API");
                return new List<PSGCData>();
            }
        }
    }
} 