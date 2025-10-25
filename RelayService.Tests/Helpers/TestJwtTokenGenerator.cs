using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace RelayService.Tests.Helpers;

/// <summary>
/// Helper class for generating JWT tokens for testing purposes
/// </summary>
public static class TestJwtTokenGenerator
{
    // Must match the secret in RelayService configuration
    private const string TestSecret = "superSecretKey@345superSecretKey@345";

    /// <summary>
    /// Generates a valid JWT token with the specified user ID
    /// </summary>
    /// <param name="userId">The user ID to include in the token claims</param>
    /// <param name="username">Optional username to include in the token claims</param>
    /// <param name="expirationMinutes">Token expiration time in minutes (default: 60)</param>
    /// <returns>A valid JWT token string</returns>
    public static string GenerateValidToken(
        string userId = "test-user-123",
        string? username = null,
        int expirationMinutes = 60)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(TestSecret);

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userId)
        };

        if (!string.IsNullOrEmpty(username))
        {
            claims.Add(new Claim(ClaimTypes.Name, username));
        }

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = DateTime.UtcNow.AddMinutes(expirationMinutes),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    /// <summary>
    /// Generates an expired JWT token for testing validation failures
    /// </summary>
    /// <param name="userId">The user ID to include in the token claims</param>
    /// <returns>An expired JWT token string</returns>
    public static string GenerateExpiredToken(string userId = "test-user-123")
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(TestSecret);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId)
            }),
            NotBefore = DateTime.UtcNow.AddMinutes(-60), // Started 60 minutes ago
            Expires = DateTime.UtcNow.AddMinutes(-10), // Expired 10 minutes ago (outside 5min ClockSkew)
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    /// <summary>
    /// Generates an invalid JWT token with wrong signature for testing
    /// </summary>
    /// <returns>A JWT token with invalid signature</returns>
    public static string GenerateInvalidToken()
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var wrongKey = Encoding.UTF8.GetBytes("wrong-secret-key-that-does-not-match");

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "test-user-123")
            }),
            Expires = DateTime.UtcNow.AddMinutes(60),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(wrongKey),
                SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }
}
