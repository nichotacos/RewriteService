using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace RewriteService.Controllers {
    [Route("api/[controller]")]
    [ApiController]
    public class LogController : ControllerBase {
        private readonly LoggerService _logger;

        public LogController(LoggerService logger) {
            _logger = logger;
        }

        [HttpPost("write-log")]
        public IActionResult WriteLog([FromQuery] string message) {
            _logger.TLog(message);
            return Ok("Log written successfully");
        }
    }
}
