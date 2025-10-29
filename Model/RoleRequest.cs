using Newtonsoft.Json;

namespace RewriteService.Model {
    public class RoleRequest {
        [JsonProperty("role_id")]
        public int RoleID { get; set; }

        [JsonProperty("role_name")]
        public string RoleName { get; set; } = string.Empty;
    }
}
