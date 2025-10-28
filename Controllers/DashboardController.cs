using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using Newtonsoft.Json;
using Org.BouncyCastle.Bcpg.OpenPgp;
using RewriteService.Model;
using System.Data;
using System.Net;

namespace RewriteService.Controllers {
    [Route("api/[controller]")]
    [ApiController]
    public class DashboardController : ControllerBase {
        private readonly IConfiguration _configuration;
        private readonly LoggerService _logger;
        private readonly string version = "1.0.1.7";

        public DashboardController(IConfiguration configuration, LoggerService loggerService) {
            _configuration = configuration;
            _logger = loggerService;
        }

        [HttpGet("request-info")]
        [HttpPost("request-info")]
        public bool isApproved(string appname) {
            string ip, hostname, fungsi, param;
            string strConn = _configuration.GetConnectionString("MySqlConn").ToString().Replace("[pwd]", "root112233");

            using MySqlConnection mysqlconn = new MySqlConnection(strConn);
            using MySqlCommand mysqlcmd = new MySqlCommand("", mysqlconn);

            try {
                ip = HttpContext.Connection.RemoteIpAddress?.ToString();
                hostname = Dns.GetHostName();
                fungsi = HttpContext.Request.Path.ToString();
                param = "";

                var allParams = new Dictionary<string, string?>();

                // Query string params
                foreach (var key in HttpContext.Request.Query.Keys) {
                    allParams[key] = HttpContext.Request.Query[key];
                }

                // POST body form-data
                if (HttpContext.Request.HasFormContentType) {
                    foreach (var key in HttpContext.Request.Form.Keys) {
                        allParams[key] = HttpContext.Request.Form[key];
                    }
                }

                // Route data
                foreach (var kvp in HttpContext.Request.RouteValues) {
                    allParams[kvp.Key] = kvp.Value?.ToString();
                }

                foreach (var kvp in HttpContext.Request.RouteValues)
                    param += $"{kvp.Key}={kvp.Value};\r\n";

                if (mysqlconn.State == ConnectionState.Closed) {
                    mysqlconn.Open();
                }

                mysqlcmd.CommandText = "SHOW TABLES LIKE 'webServiceApproval'";
                if (mysqlcmd.ExecuteScalar() == null || string.IsNullOrEmpty(mysqlcmd.ExecuteScalar().ToString())) {
                    mysqlcmd.CommandText = @"CREATE TABLE postcabang.webServiceApproval( " +
                                                "ip                 VARCHAR(20), " +
                                                "hostname           VARCHAR(20), " +
                                                "appname            VARCHAR(50), " +
                                                "addtime            DATETIME, " +
                                                "last_access        DATETIME, " +
                                                "approval           CHAR(1) DEFAULT NULL, " +
                                                "PRIMARY KEY (ip, appname) " +
                                                ")";
                    mysqlcmd.ExecuteNonQuery();
                }

                mysqlcmd.CommandText = @"CREATE TABLE IF NOT EXISTS poscabang.webServiceUsage( " +
                                            "ip                 VARCHAR(20), " +
                                            "hostname           VARCHAR(50), " +
                                            "appname            VARCHAR(50), " +
                                            "fungsi             VARCHAR(50), " +
                                            "param              TEXT, " +
                                            "addtime            DATETIME, " +
                                            "KEY idx_id(ip,hostname) " +
                                    ")";
                mysqlcmd.ExecuteNonQuery();

                mysqlcmd.CommandText = @"INSERT INTO poscabang.webServiceUsage(ip, hostname, Appname, fungsi, param, addtime) VALUES " +
                                            $"('{ip}', '{hostname}', '{appname}', '{fungsi}', '{param.Replace("\"", "").Replace("'", "")}', NOW())";
                mysqlcmd.ExecuteNonQuery();

                mysqlcmd.CommandText = @"INSERT INTO poscabang.webServiceApproval (ip, hostname, Appname, addtime, last_access, approval) VALUES " +
                                            $"('{ip}', '{hostname}', '{appname}', NOW(), NOW(), '1') ON DUPLICATE KEY UPDATE last_access=VALUES(last_access) ";
                mysqlcmd.ExecuteNonQuery();

                mysqlcmd.CommandText = $"SELECT COUNT(*) FROM poscabang.webServiceApproval WHERE ip='{ip}' AND approval='1'";

                var result = mysqlcmd.ExecuteScalar();
                var executeScalarValue = mysqlcmd.ExecuteScalar();
                var executeScalarInt = Convert.ToInt32(executeScalarValue);
                if (mysqlcmd.ExecuteScalar() != null && Convert.ToInt32(mysqlcmd.ExecuteScalar()) > 0) {
                    /*return $@"IP: {ip}" +
                        $"Hostname: {hostname}" +
                        $"Fungsi: {fungsi}" +
                        $"Param: {param}";*/

                    return true;
                }

                return false;

            } catch (Exception e) {
                _logger.TLog($"Error is Approved: {e.Message}", 3);
                return false;
            } finally {
                try {
                    mysqlconn.Close();
                } catch {
                    throw;
                }
            }
        }

        [HttpGet]
        [Route("users")]
        public IActionResult GetUsers([FromQuery] string appname) {
            string strConn = _configuration.GetConnectionString("MySqlConn2").ToString().Replace("[pwd]", "root112233");
            using MySqlConnection mysqlconn = new MySqlConnection(strConn);

            try {
                if (isApproved(appname)) {
                    using MySqlCommand mysqlcmd = new MySqlCommand("", mysqlconn);

                    if (mysqlconn.State == ConnectionState.Closed) {
                        mysqlconn.Open();
                    }

                    mysqlcmd.CommandText = "SELECT * FROM USERS";

                    using MySqlDataAdapter mysqlda = new MySqlDataAdapter();
                    DataTable dt = new DataTable();
                    mysqlda.SelectCommand = mysqlcmd;
                    mysqlda.Fill(dt);

                    if (dt.Rows.Count > 0) {
                        return Ok(JsonConvert.SerializeObject(dt));
                    } else {
                        return NotFound("No data");
                    }
                } else {
                    return Unauthorized();
                }
            } catch (Exception e) {
                return BadRequest(e.Message);
            } finally {
                try {
                    mysqlconn.Close();
                } catch {
                    throw;
                }
            }
        }

        [HttpPost]
        [Route("users")]
        public IActionResult AddUser([FromQuery] string appName, [FromBody] UserRequest user) {
            string strConn = _configuration.GetConnectionString("MySqlConn2")
                .Replace("[pwd]", "root112233");

            try {
                if (!isApproved(appName))
                    return Unauthorized();

                using var mysqlconn = new MySqlConnection(strConn);
                mysqlconn.Open();

                string sql = @"INSERT INTO USERS (username, hashed_password, first_name, last_name, role)
                       VALUES (@username, @hashed_password, @first_name, @last_name, @role)";

                using var cmd = new MySqlCommand(sql, mysqlconn);
                cmd.Parameters.AddWithValue("@username", user.Username);
                cmd.Parameters.AddWithValue("@hashed_password", user.HashedPassword);
                cmd.Parameters.AddWithValue("@first_name", user.FirstName);
                cmd.Parameters.AddWithValue("@last_name", user.LastName);
                cmd.Parameters.AddWithValue("@role", user.Role);

                int rows = cmd.ExecuteNonQuery();

                if (rows > 0)
                    return Ok(new { message = "User added successfully" });
                else
                    return BadRequest(new { message = "Insert failed" });
            } catch (Exception ex) {
                return BadRequest(new { error = ex.Message });
            }
        }
    }
}
