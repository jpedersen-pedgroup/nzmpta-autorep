namespace Autorep.Web.Services;

public class JwtSettings
{
    public string Issuer { get; set; } = "autorep";
    public string Audience { get; set; } = "autorep-api";

    // HMAC signing key. Minimum 32 chars for HMAC-SHA256. Provide via
    // configuration (User Secrets locally, Key Vault in Azure).
    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 60;     // 1h Tester (PRD §Auth)
    public int RefreshTokenDays   { get; set; } = 7;      // 7d sliding
}
