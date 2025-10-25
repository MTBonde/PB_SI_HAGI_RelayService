using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using RelayService.Configuration;
using RelayService.Interfaces;

namespace RelayService.Services;

/// <summary>
/// Service implementation for validating JSON Web Tokens (JWT) and extracting user information from token claims
/// </summary>
public class JwtValidationService : IJwtValidationService
{
    private readonly byte[] jwtSecretKeyBytes;
    private readonly JwtSecurityTokenHandler tokenHandler;
    private readonly JwtConfiguration configuration;

    /// <summary>
    /// Initializes a new instance of the JwtValidationService class with the specified configuration
    /// </summary>
    /// <param name="configuration">JWT configuration containing secret key and validation parameters</param>
    public JwtValidationService(JwtConfiguration configuration)
    {
        this.configuration = configuration;
        jwtSecretKeyBytes = Encoding.UTF8.GetBytes(configuration.Secret);
        tokenHandler = new JwtSecurityTokenHandler();
    }

    /// <summary>
    /// Validates the specified JWT token using the configured validation parameters.
    /// </summary>
    /// <param name="token">The JWT token to be validated.</param>
    /// <returns>True if the token is valid; otherwise, false.</returns>
    public bool ValidateToken(string token)
    {
        try
        {
            // Validate token signature and claims according to configuration
            tokenHandler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(jwtSecretKeyBytes),
                ValidateIssuer = configuration.ValidateIssuer,
                ValidateAudience = configuration.ValidateAudience,
                ValidateLifetime = true,
                ClockSkew = configuration.ClockSkew
            }, out _);

            return true;
        }
        catch (SecurityTokenException)
        {
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Validates a JWT token and returns the claims principal if validation succeeds.
    /// This is the preferred method for validating and extracting claims to avoid re-parsing the token.
    /// </summary>
    /// <param name="token">The JWT token string to validate</param>
    /// <param name="principal">The claims principal extracted from the validated token, or null if validation fails</param>
    /// <returns>True if the token is valid and principal is populated, false otherwise</returns>
    public bool TryValidateTokenAndGetPrincipal(string token, out ClaimsPrincipal? principal)
    {
        principal = null;
        try
        {
            // Validate token and capture the principal
            principal = tokenHandler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(jwtSecretKeyBytes),
                ValidateIssuer = configuration.ValidateIssuer,
                ValidateAudience = configuration.ValidateAudience,
                ValidateLifetime = true,
                ClockSkew = configuration.ClockSkew
            }, out _);

            return true;
        }
        catch (SecurityTokenException)
        {
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

}
