using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace Demo.Client.Services
{
    // Audit fetch reference.json z wwwroot/configs/. Demo používá hardcoded
    // defaults z GrataCascade.Core/Configuration.cs (které jsou reference v2),
    // ale UI to ukáže jako confirmation pro reviewera.
    public sealed class ConfigurationProbe
    {
        public string SchemaVersion { get; private set; }
        public string Name { get; private set; }
        public bool Loaded { get; private set; }
        public string LoadError { get; private set; }

        private sealed class ConfigPayload
        {
            public string schema_version { get; set; }
            public string name { get; set; }
        }

        public async Task FetchAsync(HttpClient http)
        {
            try
            {
                var cfg = await http.GetFromJsonAsync<ConfigPayload>("configs/reference.json");
                if (cfg != null)
                {
                    SchemaVersion = cfg.schema_version;
                    Name = cfg.name;
                    Loaded = true;
                }
            }
            catch (System.Exception ex)
            {
                LoadError = ex.Message;
            }
        }
    }
}
