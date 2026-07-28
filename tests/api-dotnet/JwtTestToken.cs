using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;

namespace EnterpriseDocumentAssistant.Api.Tests;

internal static class JwtTestToken
{
    private const string Issuer = "enterprise-document-assistant-local";
    private const string Audience = "enterprise-document-assistant-local";
    private const string SigningKey = "development-only-signing-key-change-before-production-2026";

    public static HttpClient CreateAuthenticatedClient(
        WebApplicationFactory<Program> factory,
        string userId = "legacy-system",
        string role = "User")
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Create(userId, role));
        return client;
    }

    public static string Create(string userId, string role, bool includeSubject = true)
    {
        var claims = new List<Claim>
        {
            new("name", userId),
            new("role", role)
        };

        if (includeSubject)
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Sub, userId));
        }

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            Issuer,
            Audience,
            claims,
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
