using Microsoft.AspNetCore.Mvc;
using ToBeClarify.Api.Models.Common;
using ToBeClarify.Api.Services.Client.Calendar;

namespace ToBeClarify.Api.Controllers.Client;

[ApiController]
[Route("api/client/business-calendar")]
public sealed class BusinessCalendarController(BusinessCalendarService service) : ControllerBase
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<ApiResponse<PublicBusinessCalendar>>> Get(
        [FromQuery] string? month, CancellationToken ct)
        => Ok(ApiResponse<PublicBusinessCalendar>.Ok(await service.GetAsync(month, ct)));
}
