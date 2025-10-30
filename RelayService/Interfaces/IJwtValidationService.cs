using System.Security.Claims;

namespace RelayService.Interfaces;

/// <summary>
/// Service for validating JSON Web Tokens (JWT) and extracting user information from token claims
/// </summary>
public interface IJwtValidationService
{
    /// <summary>
    /// Validates a JWT token and returns the claims principal if validation succeeds
    /// </summary>
    /// <param name="token">The JWT token string to validate</param>
    /// <param name="principal">The claims principal extracted from the validated token, or null if validation fails</param>
    /// <returns>True if the token is valid and principal is populated, false otherwise</returns>
    /// <remarks>This is the preferred method for validating and extracting claims to avoid re-parsing the token</remarks>
    bool TryValidateTokenAndGetPrincipal(string token, out ClaimsPrincipal? principal);
}
