using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Bms.IntegrationTests;

[Collection(IntegrationTestCollection.Name)]
public class AuthenticationTests
{
    private readonly HttpClient _client;

    public AuthenticationTests(BmsApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Login_ValidSeededCredentials_ReturnsAccessToken()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email = "superadmin@bms.local", password = "Passw0rd!" });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrEmpty(body!.AccessToken));
        Assert.Equal("SuperAdmin", body.User.Role);
    }

    [Fact]
    public async Task Login_WrongPassword_ReturnsBadRequestWithErrorCode()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email = "superadmin@bms.local", password = "wrong-password" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiErrorDto>();
        Assert.Equal("INVALID_CREDENTIALS", body!.ErrorCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/devices");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task HealthEndpoint_DoesNotRequireAuthentication()
    {
        // Regression test: health probes must stay reachable without a JWT
        // (found during manual testing — the auth fallback policy
        // originally caught /health too).
        var response = await _client.GetAsync("/health");

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

internal sealed record LoginResponseDto(string AccessToken, UserDto User);
internal sealed record UserDto(string Role);
internal sealed record ApiErrorDto(string ErrorCode);
