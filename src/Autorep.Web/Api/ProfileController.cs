using System.Security.Claims;
using Autorep.Web.Data;
using Autorep.Web.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Api;

// The signed-in Tester's own profile data used by the PWA. Currently just the equipment
// calibration expiry dates: these belong to the TESTER (their instruments travel with them),
// not to a farm or test. The device caches them for offline display and pushes edits back
// here; the wizard stamps a snapshot into each test at sign-off for the printed report.
[ApiController]
[Route("api/profile")]
[Authorize(Roles = Roles.Tester)]
public class ProfileController : ControllerBase
{
    private readonly AutorepDbContext _db;

    public ProfileController(AutorepDbContext db) => _db = db;

    /// <summary>ISO yyyy-MM-dd dates (DateOnly's JSON shape) — null = never recorded.</summary>
    public record CalibrationDto(DateOnly? AirFlowMeters, DateOnly? PulsatorTesters, DateOnly? VacuumGauges);

    [HttpGet("calibration")]
    public async Task<IActionResult> GetCalibration(CancellationToken ct)
    {
        var testerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var dto = await _db.Users
            .Where(u => u.Id == testerId)
            .Select(u => new CalibrationDto(u.CalAirFlowMetersExpiry, u.CalPulsatorTestersExpiry, u.CalVacuumGaugesExpiry))
            .FirstOrDefaultAsync(ct);
        if (dto is null) return NotFound();
        return Ok(dto);
    }

    // Full replace of the three dates (the device always sends the complete set). Approaching or
    // past dates are legal — expiry warns the tester but never blocks anything.
    [HttpPut("calibration")]
    public async Task<IActionResult> PutCalibration([FromBody] CalibrationDto dto, CancellationToken ct)
    {
        var testerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == testerId, ct);
        if (user is null) return NotFound();

        user.CalAirFlowMetersExpiry = dto.AirFlowMeters;
        user.CalPulsatorTestersExpiry = dto.PulsatorTesters;
        user.CalVacuumGaugesExpiry = dto.VacuumGauges;
        await _db.SaveChangesAsync(ct);

        return Ok(new CalibrationDto(user.CalAirFlowMetersExpiry, user.CalPulsatorTestersExpiry, user.CalVacuumGaugesExpiry));
    }
}
