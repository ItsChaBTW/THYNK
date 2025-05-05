using System.Text.Json.Serialization;

namespace THYNK.Models
{
    public class PSGCResponse
    {
        [JsonPropertyName("data")]
        public List<PSGCData> Data { get; set; }
    }

    public class PSGCData
    {
        [JsonPropertyName("code")]
        public string Code { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("level")]
        public string Level { get; set; }

        [JsonPropertyName("regionCode")]
        public string RegionCode { get; set; }

        [JsonPropertyName("provinceCode")]
        public string ProvinceCode { get; set; }

        [JsonPropertyName("cityMunicipalityCode")]
        public string CityMunicipalityCode { get; set; }
    }

    public class AddressModel
    {
        public string RegionCode { get; set; }
        public string RegionName { get; set; }
        public string ProvinceCode { get; set; }
        public string ProvinceName { get; set; }
        public string CityMunicipalityCode { get; set; }
        public string CityMunicipalityName { get; set; }
        public string BarangayCode { get; set; }
        public string BarangayName { get; set; }
    }
} 