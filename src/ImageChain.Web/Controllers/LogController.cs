using ImageChain.Web.Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ImageChain.Web.Controllers;

[ApiController]
[Route("api/log")]
[Authorize(AuthenticationSchemes = "AdminPassword")]
public class LogController : ControllerBase
{
    private readonly LogRingBuffer _buffer;

    public LogController(LogRingBuffer buffer)
        => _buffer = buffer;

    [HttpGet("poll")]
    public IActionResult Poll([FromQuery] long after = 0)
        => Ok(new { lines = _buffer.GetSince(after), seq = _buffer.Seq });

    [HttpPost("clear")]
    public IActionResult Clear()
    {
        _buffer.Clear();
        return Ok(new { message = "已清空日志", seq = _buffer.Seq });
    }
}