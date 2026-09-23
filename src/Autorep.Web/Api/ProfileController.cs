using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace Autorep.Web.Api;

// The signed-in Tester's own profile data used by the PWA: the equipment calibration expiry
// dates, which belong to the TESTER (their instruments travel with them), not to a farm or test,
// and their Testing Company's report branding. The device caches both for offline use and pushes
// calibration edits back here; the wizard stamps the calibration dates and the company into each
// test at sign-off for the printed report.
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

    /// <summary>The tester as the report names them, so the farmer knows who to call: name, phone,
    /// NZMPTA registration (certificate) number and its expiry. The device caches it and stamps it
    /// into each test at sign-off.</summary>
    public record TesterDetailsDto(string Name, string? Phone, string? RegistrationNumber, DateOnly? RegistrationExpiry);

    [HttpGet("tester")]
    public async Task<IActionResult> GetTester(CancellationToken ct)
    {
        var testerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var dto = await _db.Users
            .Where(u => u.Id == testerId)
            .Select(u => new TesterDetailsDto(
                u.DisplayName != "" ? u.DisplayName : (u.Email ?? u.UserName ?? ""),
                u.PhoneNumber, u.CertificateNo, u.LicenceExpiryDate))
            .FirstOrDefaultAsync(ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    /// <summary>The tester's Testing Company as the report letterhead shows it. Logo is a data URL,
    /// null when the company has none.</summary>
    public record CompanyBrandingDto(Guid Id, string Name, string? Logo);

    // The device caches this so reports carry the company logo offline. The logo can be up to
    // 1 MB, so the response carries an ETag over the name and logo and a matching If-None-Match
    // gets a bodyless 304: the device re-checks on every app load and sync, but only downloads
    // the logo again when an admin changes it. 204 means the tester has no company, and the device
    // drops whatever it had cached.
    [HttpGet("company")]
    public async Task<IActionResult> GetCompany(CancellationToken ct)
    {
        var testerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var company = await _db.Users
            .Where(u => u.Id == testerId && u.TestingCompany != null)
            .Select(u => new
            {
                u.TestingCompany!.Id,
                u.TestingCompany.Name,
                u.TestingCompany.LogoData,
                u.TestingCompany.LogoContentType,
            })
            .FirstOrDefaultAsync(ct);
        if (company is null) return NoContent();

        var etag = BrandingETag(company.Id, company.Name, company.LogoData, company.LogoContentType);
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.ETag = etag.ToString();
        if (Request.GetTypedHeaders().IfNoneMatch.Any(t => t.Compare(etag, useStrongComparison: true)))
            return StatusCode(StatusCodes.Status304NotModified);

        return Ok(new CompanyBrandingDto(company.Id, company.Name, LogoImage.DataUrl(company.LogoData, company.LogoContentType)));
    }

    private static EntityTagHeaderValue BrandingETag(Guid id, string name, byte[]? logo, string? contentType)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes($"{id}\n{name}\n{contentType}\n"));
        if (logo is not null) hash.AppendData(logo);
        return new EntityTagHeaderValue($"\"{Convert.ToHexString(hash.GetHashAndReset())[..32]}\"");
    }
}
