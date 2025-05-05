using Microsoft.AspNetCore.Mvc;
using THYNK.Services;
using THYNK.Models;

namespace THYNK.Controllers
{
    [Route("api/psgc")]
    [ApiController]
    public class PSGCController : ControllerBase
    {
        private readonly IPSGCService _psgcService;
        private readonly ILogger<PSGCController> _logger;

        public PSGCController(IPSGCService psgcService, ILogger<PSGCController> logger)
        {
            _psgcService = psgcService;
            _logger = logger;
        }

        [HttpGet("provinces")]
        public async Task<IActionResult> GetProvinces()
        {
            try
            {
                var provinces = await _psgcService.GetProvinces("130000000"); // NCR region code
                return Ok(provinces);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching provinces");
                return StatusCode(500, "Error fetching provinces");
            }
        }

        [HttpGet("cities-municipalities/{provinceCode}")]
        public async Task<IActionResult> GetCityMunicipalities(string provinceCode)
        {
            try
            {
                var citiesMunicipalities = await _psgcService.GetCityMunicipalities(provinceCode);
                return Ok(citiesMunicipalities);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching cities/municipalities");
                return StatusCode(500, "Error fetching cities/municipalities");
            }
        }

        [HttpGet("barangays/{cityMunicipalityCode}")]
        public async Task<IActionResult> GetBarangays(string cityMunicipalityCode)
        {
            try
            {
                var barangays = await _psgcService.GetBarangays(cityMunicipalityCode);
                return Ok(barangays);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching barangays");
                return StatusCode(500, "Error fetching barangays");
            }
        }
    }
} 