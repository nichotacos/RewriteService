using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using Newtonsoft.Json;
using Org.BouncyCastle.Bcpg.OpenPgp;
using RewriteService.Model;
using System.Data;
using System.Net;
using BCrypt.Net;
using Org.BouncyCastle.Crypto.Generators;
using System.IdentityModel;
using Org.BouncyCastle.Bcpg;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using System.Collections.Concurrent;

namespace RewriteService.Controllers {
    [Route("api/[controller]")]
    [ApiController]
    public class DashboardController : ControllerBase {
        private readonly IConfiguration _configuration;
        private readonly LoggerService _logger;
        private readonly string version = "1.0.1.7";

        private static ConcurrentDictionary<string, string> sessions = new ConcurrentDictionary<string, string>();

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
        public IActionResult GetUsers([FromQuery] string appname, [FromHeader(Name = "X-Session-Token")] string token) {
            string strConn = _configuration.GetConnectionString("MySqlConn2").ToString().Replace("[pwd]", "root112233");
            using MySqlConnection mysqlconn = new MySqlConnection(strConn);

            try {
                if (isApproved(appname) && IsValidToken(token)) {

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
                        return Ok(new List<object>());
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

                string hashedPassword = BCrypt.Net.BCrypt.HashPassword(user.HashedPassword);

                string sql = @"INSERT INTO USERS (username, hashed_password, first_name, last_name, role_id)
                       VALUES (@username, @hashed_password, @first_name, @last_name, @role_id)";

                using var cmd = new MySqlCommand(sql, mysqlconn);
                cmd.Parameters.AddWithValue("@username", user.Username);
                cmd.Parameters.AddWithValue("@hashed_password", hashedPassword);
                cmd.Parameters.AddWithValue("@first_name", user.FirstName);
                cmd.Parameters.AddWithValue("@last_name", user.LastName);
                cmd.Parameters.AddWithValue("@role_id", user.RoleID);

                int rows = cmd.ExecuteNonQuery();

                if (rows > 0)
                    return Ok(new { message = "User added successfully" });
                else
                    return BadRequest(new { message = "Insert failed" });
            } catch (Exception ex) {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPost]
        [Route("login")]
        public async Task<IActionResult> Login([FromQuery] string appName, [FromBody] LoginRequest loginData) {
            string strConn = _configuration.GetConnectionString("MySqlConn2")
                .Replace("[pwd]", "root112233");

            try {
                if (!isApproved(appName)) return Unauthorized();

                using var mysqlconn = new MySqlConnection(strConn);
                await mysqlconn.OpenAsync();

                string sql = "SELECT user_id, username, hashed_password, first_name, last_name, role_id FROM USERS WHERE username = @username";
                using var cmd = new MySqlCommand(sql, mysqlconn);
                cmd.Parameters.AddWithValue("@username", loginData.Username);

                using var reader = await cmd.ExecuteReaderAsync();
                if (!reader.Read()) return Unauthorized(new {
                    message = "Username tidak ditemukan"
                });

                string hashedPassword = reader.GetString("hashed_password");
                if (!BCrypt.Net.BCrypt.Verify(loginData.Password, hashedPassword)) {
                    return Unauthorized("Invalid password");
                }

                var token = GenerateJwtToken(reader.GetInt32("user_id"), reader.GetString("username"));
                sessions["authToken"] = token;


				return Ok(new {
                    token,
                    username = reader.GetString("username"),
                    firstname = reader.GetString("first_name"),
                    lastname = reader.GetString("last_name"),
                    role_id = reader.GetInt32("role_id")
                });
            } catch (Exception ex) {
                return BadRequest(ex.Message);
            }
        }

        public static bool IsValidToken(string token) => sessions.Values.Contains(token);

        [HttpPut]
        [Route("users")]
        public IActionResult UpdateUser([FromQuery] string appName, [FromBody] UserRequest user) {
            string strConn = _configuration.GetConnectionString("MySqlConn2")
                .Replace("[pwd]", "root112233");

            try {
                if (!isApproved(appName))
                    return Unauthorized();

                using var mysqlconn = new MySqlConnection(strConn);
                mysqlconn.Open();

                string sql = @"
                    UPDATE USERS 
                    SET username = @username, 
                        hashed_password = @hashed_password,
                        first_name = @first_name,
                        last_name = @last_name,
                        role_id = @role_id
                    WHERE user_id = @id";

                using var cmd = new MySqlCommand(sql, mysqlconn);
                cmd.Parameters.AddWithValue("@username", user.Username);
                cmd.Parameters.AddWithValue("@hashed_password", user.HashedPassword);
                cmd.Parameters.AddWithValue("@first_name", user.FirstName);
                cmd.Parameters.AddWithValue("@last_name", user.LastName);
                cmd.Parameters.AddWithValue("@role_id", user.RoleID);
                cmd.Parameters.AddWithValue("@id", user.UserID);

                int rowsAffected = cmd.ExecuteNonQuery();

                if (rowsAffected > 0) {
                    return Ok(new {
                        message = "User updated successfully",
                        updatedUser = new {
                            user.UserID,
                            user.Username,
                            user.FirstName,
                            user.LastName,
                            user.RoleID
                        }
                    });
                } else {
                    return NotFound(new { message = "User not found or no changes made" });
                }
            } catch (Exception ex) {
                return BadRequest(ex.Message);
            }
        }

        [HttpDelete]
        [Route("users")]
        public IActionResult DeleteUser([FromQuery] string appName, [FromQuery] int id) {
            string strConn = _configuration.GetConnectionString("MySqlConn2")
                .Replace("[pwd]", "root112233");

            try {
                if (!isApproved(appName)) return Unauthorized();

                using var mysqlconn = new MySqlConnection(strConn);
                if (mysqlconn.State != ConnectionState.Open) {
                    mysqlconn.Open();
                }

                string sql = "DELETE FROM USERS" +
                    " WHERE user_id = @id";

                using var cmd = new MySqlCommand(sql, mysqlconn);
                cmd.Parameters.AddWithValue("@id", id);

                int rowsAffected = cmd.ExecuteNonQuery();

                if (rowsAffected > 0) {
                    return Ok(new {
                        message = "User deleted successfully",
                    });
                } else {
                    return NotFound(new {
                        message = $"User with ID: {id} does not exist"
                    });
                }
            } catch (Exception ex) {
                return BadRequest(ex.Message);
            }
        }


        // Roles
        [HttpGet]
        [Route("roles")]
        public async Task<IActionResult> GetRoles([FromQuery] string appName) {
            string strConn = _configuration.GetConnectionString("MySqlConn2")
                .Replace("[pwd]", "root112233");

            try {
                if (!isApproved(appName))
                    return Unauthorized();

                var roles = new List<RoleRequest>();

                using var conn = new MySqlConnection(strConn);
                await conn.OpenAsync();

                string sql = "SELECT role_id, role_name FROM roles";
                using var cmd = new MySqlCommand(sql, conn);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync()) {
                    roles.Add(new RoleRequest {
                        RoleID = reader.GetInt32("role_id"),
                        RoleName = reader.GetString("role_name")
                    });
                }

                if (roles.Count == 0)
                    return NotFound("No data");

                return Ok(roles);
            } catch (Exception ex) {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPost]
        [Route("roles")]
        public IActionResult AddUser([FromQuery] string appName, [FromBody] RoleRequest role) {
            string strConn = _configuration.GetConnectionString("MySqlConn2")
                .Replace("[pwd]", "root112233");

            try {
                if (!isApproved(appName)) return Unauthorized();

                using var mysqlconn = new MySqlConnection(strConn);
                mysqlconn.Open();

                string sql = "INSERT INTO ROLES (role_name) VALUES (@role_name)";

                using var cmd = new MySqlCommand(sql, mysqlconn);
                cmd.Parameters.AddWithValue("@role_name", role.RoleName);

                int rowCounte = cmd.ExecuteNonQuery();

                if (rowCounte > 0)
                    return Ok(new { message = "Role added successfully" });
                else
                    return BadRequest(new { message = "Insert failed" });

            } catch (Exception ex) {
                return BadRequest(ex.Message);
            }
        }

        [HttpPut]
        [Route("roles")]
        public IActionResult UpdateRole([FromQuery] string appName, [FromBody] RoleRequest role) {
            string strConn = _configuration.GetConnectionString("MySqlConn2")
                .Replace("[pwd]", "root112233");

            try {
                if (!isApproved(appName)) return Unauthorized();

                using var mysqlconn = new MySqlConnection(strConn);
                mysqlconn.Open();

                string sql = "UPDATE ROLES" +
                    " SET role_name = @role_name" +
                    " WHERE role_id = @role_id";

                using var cmd = new MySqlCommand(sql, mysqlconn);
                cmd.Parameters.AddWithValue("@role_name", role.RoleName);
                cmd.Parameters.AddWithValue("@role_id", role.RoleID);

                int rowCounts = cmd.ExecuteNonQuery();

                if (rowCounts > 0) {
                    return Ok(new { message = "Role updated successfully" });
                } else {
                    return BadRequest(new {
                        message = "Failed to update role"
                    });
                }
            } catch (Exception ex) {
                return BadRequest(ex.Message);
            }
        }

        [HttpDelete]
        [Route("roles")]
        public IActionResult DeleteRole([FromQuery] string appName, [FromQuery] int role_id) {
            string strConn = _configuration.GetConnectionString("MySqlConn2")
                .Replace("[pwd]", "root112233");

            try {
                if (!isApproved(appName)) return Unauthorized();

                using var mysqlconn = new MySqlConnection(strConn);
                mysqlconn.Open();

                string sql = "DELETE FROM roles WHERE role_id = @role_id";
                using var cmd = new MySqlCommand(sql, mysqlconn);

                cmd.Parameters.AddWithValue("@role_id", role_id);

                int rowCounts = cmd.ExecuteNonQuery();

                if (rowCounts > 0) {
                    return Ok(new {
                        message = "Role deleted successfully"
                    });
                } else {
                    return BadRequest(new {
                        message = "Failed to delete role"
                    });
                } 
            } catch (Exception ex) { 
                return BadRequest(ex.Message); 
            }
        }

        private string GenerateJwtToken(int userId, string username) {
            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("c46b43c50ee64bd60935d4be5e604309"));
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

            var claims = new[] {
                new Claim(JwtRegisteredClaimNames.Sub, username),
                new Claim("userId", userId.ToString())
            };

            var token = new JwtSecurityToken(
                issuer: "Dashboard Management",
                audience: "Dashboard Management",
                claims: claims,
                expires: DateTime.Now.AddHours(4),
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
