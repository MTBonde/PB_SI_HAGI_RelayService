namespace RelayService.Configuration;

/// <summary>
/// Configuration settings for JWT (JSON Web Token) validation and authentication
/// </summary>
public class JwtConfiguration
{
    /// <summary>
    /// Gets or sets the secret key used for JWT signature validation
    /// </summary>
    /// <remarks>T0DO: Move to secure secret storage (e.g., Azure Key Vault, AWS Secrets Manager)</remarks>
    public string Secret { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether to validate the token issuer claim
    /// </summary>
    public bool ValidateIssuer { get; set; } = false;

    /// <summary>
    /// Gets or sets whether to validate the token audience claim
    /// </summary>
    public bool ValidateAudience { get; set; } = false;

    /// <summary>
    /// Gets or sets the clock skew tolerance for token expiration validation
    /// </summary>
    /// <remarks>Default 5 minutes to account for clock differences between systems</remarks>
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromMinutes(5);
}
