using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

#nullable disable

namespace GTAServer
{
    /// <summary>IP geolocation for Discord log lines (ip-api.com, no key).</summary>
    public static class GeoIpLookup
    {
        private static readonly HttpClient HttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        private const string IpApiUrl = "http://ip-api.com/json";

        private static bool IsPrivateOrLocal(string ip)
        {
            if (string.IsNullOrEmpty(ip)) return true;
            string s = ip;
            int idx = ip.LastIndexOf(':');
            if (idx >= 0 && ip.Length > idx + 1) s = ip.Substring(idx + 1);
            if (s == "127.0.0.1" || s == "::1" || s == "localhost") return true;
            if (IPAddress.TryParse(s, out var addr))
            {
                if (IPAddress.IsLoopback(addr)) return true;
                byte[] b = addr.GetAddressBytes();
                if (b.Length == 4)
                {
                    if (b[0] == 10) return true;
                    if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;
                    if (b[0] == 192 && b[1] == 168) return true;
                }
            }
            return false;
        }

        /// <summary>Returns e.g. "country: US, region: California, city: Los Angeles, isp: Comcast" or "geo: unavailable".</summary>
        public static async Task<string> GetGeoAsync(string ip)
        {
            if (IsPrivateOrLocal(ip)) return "geo: local/private";
            try
            {
                var url = $"{IpApiUrl}/{Uri.EscapeDataString(ip)}?fields=status,country,countryCode,regionName,city,isp,org";
                var json = await HttpClient.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("status", out var status) && status.GetString() != "success")
                    return "geo: unavailable";
                var parts = new List<string>();
                if (root.TryGetProperty("country", out var country))
                {
                    string cc = root.TryGetProperty("countryCode", out var ccEl) ? ccEl.GetString() : "";
                    parts.Add(string.IsNullOrEmpty(cc) ? $"country: {country.GetString()}" : $"country: {country.GetString()} ({cc})");
                }
                if (root.TryGetProperty("regionName", out var region))
                    parts.Add($"region: {region.GetString()}");
                if (root.TryGetProperty("city", out var city))
                    parts.Add($"city: {city.GetString()}");
                if (root.TryGetProperty("isp", out var isp))
                    parts.Add($"isp: {isp.GetString()}");
                else if (root.TryGetProperty("org", out var org))
                    parts.Add($"org: {org.GetString()}");
                return parts.Count > 0 ? string.Join(", ", parts) : "geo: no data";
            }
            catch
            {
                return "geo: unavailable";
            }
        }
    }
}
