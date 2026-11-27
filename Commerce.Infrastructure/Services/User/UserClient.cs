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

    public async Task<SellerShopResult> GetSellerShopAsync(Guid sellerUserId, CancellationToken ct = default)
    {
        using var response = await _httpClient.GetAsync($"api/v1/internal/users/{sellerUserId}/shop", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return new SellerShopResult(false, null);
        response.EnsureSuccessStatusCode();   // anything else is a temporary failure: the caller retries

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
            root = data;

        string? shopName = null;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("shopName", out var shopNameProp) && shopNameProp.ValueKind == JsonValueKind.String)
        {
            shopName = shopNameProp.GetString();
        }

        return root.ValueKind == JsonValueKind.Object && root.TryGetProperty("shopId", out var shopId)
            && shopId.ValueKind == JsonValueKind.String && Guid.TryParse(shopId.GetString(), out var parsed) && parsed != Guid.Empty
                ? new SellerShopResult(true, parsed, shopName)
                : new SellerShopResult(false, null);
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

    public async Task<PenaltyRevokeResponse?> RevokeBuyerRefusalPenaltyAsync(
        PenaltyRevokeRequest request,
        CancellationToken ct = default)
    {
        try
        {
            using var response = await _httpClient.PostAsJsonAsync("api/v1/internal/users/reputation-penalty-revoke", request, JsonOptions, ct);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<PenaltyRevokeResponse>(JsonOptions, ct);
            }

            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("[REST UserClient] Withdrawing the refusal penalty of '{Reference}' failed: {Status} - {Error}", request.ReferenceId, response.StatusCode, errorBody);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[REST UserClient] Error connecting to UserService while withdrawing the refusal penalty of '{Reference}'.", request.ReferenceId);
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

    public async Task<BuyerAddressDto?> GetBuyerAddressAsync(Guid buyerId, Guid addressId, CancellationToken ct = default)
    {
        try
        {
            var url = $"api/v1/internal/users/{buyerId}/addresses/{addressId}";
            using var response = await _httpClient.GetAsync(url, ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[REST UserClient] GetBuyerAddress returned status {StatusCode} for buyer '{BuyerId}', address '{AddressId}'.",
                    response.StatusCode, buyerId, addressId);
                throw new CommerceDomainException("USER_SERVICE_UNAVAILABLE", "User service is temporarily unavailable. Please try again later.", 503);
            }

            return await ParseAddressDtoAsync(response, ct);
        }
        catch (CommerceDomainException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[REST UserClient] Error connecting to UserService during address retrieval for buyer '{BuyerId}'.", buyerId);
            throw new CommerceDomainException("USER_SERVICE_UNAVAILABLE", "User service is temporarily unavailable. Please try again later.", 503);
        }
    }

    public async Task<BuyerAddressDto?> GetBuyerDefaultAddressAsync(Guid buyerId, CancellationToken ct = default)
    {
        try
        {
            var url = $"api/v1/internal/users/{buyerId}/addresses/default";
            using var response = await _httpClient.GetAsync(url, ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[REST UserClient] GetBuyerDefaultAddress returned status {StatusCode} for buyer '{BuyerId}'.",
                    response.StatusCode, buyerId);
                throw new CommerceDomainException("USER_SERVICE_UNAVAILABLE", "User service is temporarily unavailable. Please try again later.", 503);
            }

            return await ParseAddressDtoAsync(response, ct);
        }
        catch (CommerceDomainException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[REST UserClient] Error connecting to UserService during default address retrieval for buyer '{BuyerId}'.", buyerId);
            throw new CommerceDomainException("USER_SERVICE_UNAVAILABLE", "User service is temporarily unavailable. Please try again later.", 503);
        }
    }

    private static async Task<BuyerAddressDto?> ParseAddressDtoAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var rawBody = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(rawBody)) return null;

        using var doc = JsonDocument.Parse(rawBody);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
        {
            root = data;
        }

        return root.ValueKind == JsonValueKind.Object ? MapAddress(root) : null;
    }

    /// <summary>
    /// UserService answers with the address-book names (addressId, addressLine, wardName, districtName, provinceName, provinceId, districtId, wardCode);
    /// the older Ghn-prefixed names are still read. Reading only the older names left the district and ward at 0, so an address-book address was refused.
    /// </summary>
    private static BuyerAddressDto MapAddress(JsonElement e)
    {
        string? Text(params string[] names)
        {
            foreach (var name in names)
            {
                foreach (var property in e.EnumerateObject())
                {
                    if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
                    {
                        return property.Value.GetString();
                    }
                }
            }

            return null;
        }

        int? Number(params string[] names)
        {
            foreach (var name in names)
            {
                foreach (var property in e.EnumerateObject())
                {
                    if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var value))
                    {
                        return value;
                    }
                }
            }

            return null;
        }

        bool Flag(string name)
        {
            foreach (var property in e.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return property.Value.ValueKind == JsonValueKind.True;
                }
            }

            return false;
        }

        Guid id = Guid.TryParse(Text("addressId", "id"), out var parsed) ? parsed : Guid.Empty;
        var street = Text("addressLine", "street") ?? string.Empty;
        var ward = Text("wardName", "ward") ?? string.Empty;
        var district = Text("districtName", "district") ?? string.Empty;
        var province = Text("provinceName", "province") ?? string.Empty;
        var full = Text("fullAddressText") ?? string.Join(", ", new[] { street, ward, district, province }.Where(x => !string.IsNullOrWhiteSpace(x)));

        return new BuyerAddressDto(
            Id: id,
            RecipientName: Text("recipientName") ?? string.Empty,
            Phone: Text("phone") ?? string.Empty,
            Street: street,
            Ward: ward,
            District: district,
            Province: province,
            GhnDistrictId: Number("districtId", "ghnDistrictId") ?? 0,
            GhnWardCode: Text("wardCode", "ghnWardCode") ?? string.Empty,
            FullAddressText: full,
            IsDefault: Flag("isDefault"),
            ProvinceId: Number("provinceId"));
    }
}
