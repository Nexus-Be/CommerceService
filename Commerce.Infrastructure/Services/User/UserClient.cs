namespace Commerce.Infrastructure.Services.User;

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Commerce.Application.Interfaces;
using Commerce.Domain.Exceptions;

public sealed class UserClient : IUserClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<UserClient> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public UserClient(HttpClient httpClient, ILogger<UserClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<UserPermissionCheckResult> CheckBuyerPermissionAsync(
        Guid buyerId,
        string action = "buy",
        CancellationToken ct = default)
    {
        try
        {
            var url = $"api/v1/internal/users/{buyerId}/permission-check?action={Uri.EscapeDataString(action)}";
            var response = await _httpClient.GetAsync(url, ct);

            if (response.IsSuccessStatusCode)
            {
                return new UserPermissionCheckResult(true, null, null);
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                string? errorCode = "BUYER_ACTION_RESTRICTED";
                string? message = "User has an active restriction prohibiting purchase actions.";

                try
                {
                    var rawBody = await response.Content.ReadAsStringAsync(ct);
                    if (!string.IsNullOrWhiteSpace(rawBody))
                    {
                        using var doc = JsonDocument.Parse(rawBody);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("errorCode", out var codeElem) && !string.IsNullOrWhiteSpace(codeElem.GetString()))
                        {
                            errorCode = codeElem.GetString();
                        }
                        if (root.TryGetProperty("detail", out var detailElem) && !string.IsNullOrWhiteSpace(detailElem.GetString()))
                        {
                            message = detailElem.GetString();
                        }
                        else if (root.TryGetProperty("message", out var msgElem) && !string.IsNullOrWhiteSpace(msgElem.GetString()))
                        {
                            message = msgElem.GetString();
                        }
                    }
                }
                catch
                {
                    // Fallback to default message
                }

                _logger.LogWarning("[REST UserClient] Buyer '{BuyerId}' permission check failed: {ErrorCode} - {Message}", buyerId, errorCode, message);
                return new UserPermissionCheckResult(false, errorCode, message);
            }

            _logger.LogWarning("[REST UserClient] Permission check returned status {StatusCode} for buyer '{BuyerId}'.", response.StatusCode, buyerId);
            throw new CommerceDomainException("USER_SERVICE_UNAVAILABLE", "User service is temporarily unavailable. Please try again later.", 503);
        }
        catch (CommerceDomainException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[REST UserClient] Error connecting to UserService during permission check for buyer '{BuyerId}'.", buyerId);
            throw new CommerceDomainException("USER_SERVICE_UNAVAILABLE", "User service is temporarily unavailable. Please try again later.", 503);
        }
    }

    public async Task<ReputationAdjustResponse?> AdjustReputationAsync(
        ReputationAdjustRequest request,
        CancellationToken ct = default)
    {
        try
        {
            const string url = "api/v1/internal/users/reputation-adjust";
            var response = await _httpClient.PostAsJsonAsync(url, request, JsonOptions, ct);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<ReputationAdjustResponse>(JsonOptions, ct);
                _logger.LogInformation("[REST UserClient] Reputation adjusted for Seller '{UserId}', Delta: {AppliedDelta}, IsDuplicate: {IsDuplicate}, CooldownSuppressed: {CooldownSuppressed}",
                    request.UserId, result?.AppliedDelta, result?.IsDuplicate, result?.CooldownSuppressed);
                return result;
            }

            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("[REST UserClient] Reputation adjust failed for Seller '{UserId}': {Status} - {Error}", request.UserId, response.StatusCode, errorBody);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[REST UserClient] Error connecting to UserService during reputation adjustment for Seller '{UserId}'.", request.UserId);
            return null;
        }
    }

    public async Task<UserPenaltyResponse?> CreatePenaltyAsync(
        UserPenaltyRequest request,
        CancellationToken ct = default)
    {
        try
        {
            const string url = "api/v1/internal/users/penalties";
            var response = await _httpClient.PostAsJsonAsync(url, request, JsonOptions, ct);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<UserPenaltyResponse>(JsonOptions, ct);
                _logger.LogInformation("[REST UserClient] Penalty recorded for Seller '{UserId}', PenaltyId: {PenaltyId}, Delta: {AppliedDelta}",
                    request.UserId, result?.PenaltyId, result?.AppliedDelta);
                return result;
            }

            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("[REST UserClient] Penalty creation failed for Seller '{UserId}': {Status} - {Error}", request.UserId, response.StatusCode, errorBody);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[REST UserClient] Error connecting to UserService during penalty creation for Seller '{UserId}'.", request.UserId);
            return null;
        }
    }
}
