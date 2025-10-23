using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using Mysqlx.Crud;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Data;
using System.Net;
using System.Text.Json.Serialization;

namespace RewriteService.Controllers {
    [Route("api/[controller]")]
    [ApiController]
    public class MainController : ControllerBase {
        private readonly IConfiguration _configuration;

        public MainController(IConfiguration configuration) {
            _configuration = configuration;
        }

        [HttpGet("request-info")]
        [HttpPost("request-info")]
        public bool isApproved(string appname) {
            string ip, hostname, fungsi, param;
            string strConn = _configuration.GetConnectionString("MySqlConn");

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

                mysqlcmd.CommandText = @"INSERT INTO poscbang.webServiceUsage(ip, hostname, Appname, fungsi, param, addtime) VALUES " +
                                            $"('{ip}, {hostname}, {appname}, {fungsi}, {param.Replace("\"", "").Replace("'", "")} ', NOW())";
                mysqlcmd.ExecuteNonQuery();

                mysqlcmd.CommandText = @"INSERT INTO poscabang.webServiceApproval (ip, hostname, Appname, addtime, last_access) VALUES " +
                                            $"('{ip}, {hostname}, {appname}, 'NOW(), NOW()) ON DUPLICATE KEY UPDATE last_access=VALUES(last_access) ";
                mysqlcmd.ExecuteNonQuery();

                mysqlcmd.CommandText = $"SELECT COUNT(*) FROM poscabang.webServiceApproval WHERE ip='{ip}' AND approval='1'";

                var result = mysqlcmd.ExecuteScalar();
                if (mysqlcmd.ExecuteScalar() != null && Convert.ToInt32(mysqlcmd.ExecuteScalar()) > 0) {
                    /*return $@"IP: {ip}" +
                        $"Hostname: {hostname}" +
                        $"Fungsi: {fungsi}" +
                        $"Param: {param}";*/

                    return true;
                }

                return false;
 
            } catch (Exception e){
                throw;
            } finally {
                try {
                    mysqlconn.Close();
                } catch (Exception e) {
                    
                }
            }
        }

        [HttpGet]
        public IActionResult GetNPout(string appname, string toko, string dc, string tipeNP) {
            string connStr = _configuration.GetConnectionString("MySqlConn");
            using MySqlConnection mysqlconn = new MySqlConnection();

            try {
                if (isApproved(appname)) {
                    using MySqlCommand mysqlcmd = new MySqlCommand("", mysqlconn);

                    if (mysqlconn.State == ConnectionState.Closed) {
                        mysqlconn.Open();
                    }

                    /*22/01/2025
                    Revisi utk ubah sumber data brd_npb_all ke brd_npb_detil / sum
                    mysqlcmd.CommandText = "SELECT plu AS prdcd, SUM(qty) AS qty " &
                                           "FROM poscabang.brd_npb_all " &
                                           "WHERE kodedc='" & dc & "' AND toko='" & toko & "' AND namafile LIKE '" & tipeNP & " % ' AND tgl_bpb_toko IS NULL AND tgl_docno >= DATE_ADD(CURDATE(), INTERVAL -3 DAY) " &
                                           "GROUP BY plu "*/

                    if (tipeNP.Substring(0, 3) == "NPR") {
                        mysqlcmd.CommandText = "SELECT plu AS prdcd, SUM(qty) AS qty";
                        mysqlcmd.CommandText += " FROM poscabang.brd_npr_sum a LEFT JOIN poscabang.brd_npr_detil b";
                        mysqlcmd.CommandText += " ON a.namafile = b.namafile ";
                        mysqlcmd.CommandText += $" WHERE a.kodedc='{dc}' AND a.toko='{toko}' AND a.namafile LIKE '{toko}%' AND bukti_tgl_bpb IS NULL AND a.tgl_docno >= DATE_ADD(CURDATE(), INTERVAL -3 DAY) ";
                        mysqlcmd.CommandText += " GROUP BY plu ";
                    } else {
                        /*23 / 6 / 2025
                        permintaan OPR: tenggang 3 hari menjadi 28 hari TERKAIT npb outsanding*/
                        mysqlcmd.CommandText = "SELECT plu as prdcd, SUM(qty) AS qty ";
                        mysqlcmd.CommandText += " FROM poscabang.brd_npb_sum a LEFT JOIN poscabang.brd_npb_detil b";
                        mysqlcmd.CommandText += " ON a.namafile = b.namafile ";
                        mysqlcmd.CommandText += $" WHERE a.kodedc='{dc}' AND a.toko='{toko}' AND a.namafile LIKE '{tipeNP}%' AND bukti_tgl_bpb IS NULL AND a.tgl_docno >= DATE_ADD(CURDATE(), INTERVAL -3 DAY) ";
                        mysqlcmd.CommandText += " GROUP BY plu";
                    }

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
                throw;
            } finally {
                try {
                    mysqlconn.Close();
                } catch (Exception e) {
                    throw;
                }
            }
        }   
    }
}
