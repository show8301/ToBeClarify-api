using Microsoft.AspNetCore.Mvc;
using ToBeClarify.Api.Infrastructure;
using ToBeClarify.Api.Models.Common;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Services.Client.Reservations;

namespace ToBeClarify.Api.Controllers.Client;

[ApiController]
[Route("api/client/staff-reservations")]
public sealed class StaffReservationsController : ControllerBase
{
    private readonly IReservationService _service;
    private readonly IAppClock _clock;

    public StaffReservationsController(IReservationService service, IAppClock clock)
    {
        _service = service;
        _clock = clock;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<StaffReservationDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<StaffReservationDto>>>> Get(
        [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, CancellationToken cancellationToken)
    {
        var rangeStart = from ?? StartOfDay(_clock.Now);
        var rangeEnd = to ?? rangeStart.AddDays(7);
        return Ok(ApiResponse<IReadOnlyList<StaffReservationDto>>.Ok(
            await _service.GetStaffReservationsAsync(rangeStart, rangeEnd, cancellationToken)));
    }

    private static DateTimeOffset StartOfDay(DateTimeOffset value)
        => new(value.Year, value.Month, value.Day, 0, 0, 0, value.Offset);
}
