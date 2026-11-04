namespace Commerce.Infrastructure.Services.Catalog;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Commerce.Application.Interfaces;
using Commerce.Domain.Exceptions;

/// <summary>
/// REST client interacting with Catalog service for SKU Master Data, Batch Variant Lookups,
/// and Variant Switch Validation.
/// </summary>
public sealed class CatalogClient : ICatalogClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<CatalogClient> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public CatalogClient(HttpClient httpClient, ILogger<CatalogClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<CatalogProductCheckResult?> GetSkuDetailsAsync(
        Guid skuId,
        CancellationToken cancellationToken = default)
    {
        var batch = await GetSkuBatchDetailsAsync(new[] { skuId }, cancellationToken);
        return batch.TryGetValue(skuId, out var result) ? result : null;
    }

    public async Task<IReadOnlyDictionary<Guid, CatalogProductCheckResult>> GetSkuBatchDetailsAsync(
        IEnumerable<Guid> skuIds,
        CancellationToken cancellationToken = default)
    {
        var distinctIds = skuIds.Distinct().ToList();
        var resultMap = new Dictionary<Guid, CatalogProductCheckResult>();

        if (distinctIds.Count == 0)
        {
            return resultMap;
        }

        // Chunk vào các batch tối đa 100 IDs theo quy định của Catalog Service
        const int batchSize = 100;
        for (int i = 0; i < distinctIds.Count; i += batchSize)
        {
            var chunk = distinctIds.Skip(i).Take(batchSize).ToList();
            try
            {
                var payload = new { variantIds = chunk };
                var response = await _httpClient.PostAsJsonAsync("api/v1/internal/variants", payload, JsonOptions, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var data = await response.Content.ReadFromJsonAsync<CatalogBatchVariantResponse>(JsonOptions, cancellationToken);
                    if (data?.Items != null)
                    {
                        foreach (var item in data.Items)
                        {
                            // Tôn trọng Single Source of Truth từ CatalogService: nhận trực tiếp IsPurchasable và Price ?? 0m
                            bool isPurchasable = item.IsPurchasable;
                            decimal unitPrice = item.Price ?? 0m;

                            resultMap[item.VariantId] = new CatalogProductCheckResult(
                                SkuId: item.VariantId,
                                ProductId: item.ProductId,
                                SellerId: item.SellerId,
                                ProductName: !string.IsNullOrWhiteSpace(item.ProductName)
                                    ? item.ProductName
                                    : (!string.IsNullOrWhiteSpace(item.VariantName) ? item.VariantName : $"Product {item.VariantId.ToString()[..8]}"),
                                UnitPrice: unitPrice,
                                IsPurchasable: isPurchasable,
                                StockQuantity: 100);
                        }
                    }
                    continue;
                }

                _logger.LogWarning("[REST Catalog Client] Batch lookup returned status {StatusCode} for chunk size {Count}.", response.StatusCode, chunk.Count);
                throw new CommerceDomainException("CATALOG_SERVICE_UNAVAILABLE", "Product catalog service is temporarily unreachable.", 503);
            }
            catch (CommerceDomainException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[REST Catalog Client] Connection error during batch SKU lookup for chunk size {Count}.", chunk.Count);
                throw new CommerceDomainException("CATALOG_SERVICE_UNAVAILABLE", "Connection error communicating with product catalog service.", 503);
            }
        }

        return resultMap;
    }

    public async Task<CatalogVariantSwitchResult> ValidateVariantSwitchAsync(
        Guid currentSkuId,
        Guid newSkuId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new
            {
                currentSkuId,
                newSkuId
            };

            var response = await _httpClient.PostAsJsonAsync("api/v1/internal/catalog/skus/validate-variant-switch", payload, JsonOptions, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var data = await response.Content.ReadFromJsonAsync<CatalogSwitchRawResponse>(JsonOptions, cancellationToken);
                if (data != null)
                {
                    return new CatalogVariantSwitchResult(
                        IsValid: data.IsValid,
                        ProductId: data.ProductId,
                        SellerId: data.SellerId,
                        NewSkuId: data.NewSkuId,
                        NewSkuName: data.NewSkuName ?? "New Variant",
                        NewUnitPrice: data.NewUnitPrice,
                        IsPurchasable: data.IsPurchasable,
                        WeightGrams: data.WeightGrams > 0 ? data.WeightGrams : 200);
                }

                throw new CommerceDomainException("CATALOG_SERVICE_UNAVAILABLE", "Product catalog service returned invalid response payload.", 503);
            }
            else if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("[REST Catalog Client] Variant switch rejected with 400: {Body}", errorBody);

                string errorCode = "CATALOG_VARIANT_MISMATCH";
                string errorMessage = "New SKU does not belong to the same parent product.";

                try
                {
                    using var doc = JsonDocument.Parse(errorBody);
                    if (doc.RootElement.TryGetProperty("errorCode", out var codeElem))
                    {
                        errorCode = codeElem.GetString() ?? errorCode;
                    }
                    if (doc.RootElement.TryGetProperty("detail", out var detailElem))
                    {
                        errorMessage = detailElem.GetString() ?? errorMessage;
                    }
                    else if (doc.RootElement.TryGetProperty("message", out var msgElem))
                    {
                        errorMessage = msgElem.GetString() ?? errorMessage;
                    }
                }
                catch
                {
                    // Giữ giá trị mặc định nếu JSON không chuẩn
                }

                throw new CommerceDomainException(errorCode, errorMessage, 400);
            }
            else if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new CommerceDomainException("CATALOG_SKU_NOT_FOUND", "The specified product SKU was not found in catalog.", 404);
            }
            else
            {
                _logger.LogWarning("[REST Catalog Client] Variant switch returned status {StatusCode}.", response.StatusCode);
                throw new CommerceDomainException("CATALOG_SERVICE_UNAVAILABLE", "Product catalog service is temporarily unreachable.", 503);
            }
        }
        catch (CommerceDomainException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[REST Catalog Client] Connection error validating variant switch from '{CurrentSkuId}' to '{NewSkuId}'.",
                currentSkuId, newSkuId);
            throw new CommerceDomainException("CATALOG_SERVICE_UNAVAILABLE", "Connection error communicating with product catalog service.", 503);
        }
    }

    private sealed class CatalogBatchVariantResponse
    {
        public List<CatalogVariantRawItem>? Items { get; set; }
        public List<Guid>? NotFoundIds { get; set; }
    }

    private sealed class CatalogVariantRawItem
    {
        public Guid VariantId { get; set; }
        public Guid ProductId { get; set; }
        public Guid SellerId { get; set; }
        public string? ProductName { get; set; }
        public string? VariantName { get; set; }
        public string? SkuCode { get; set; }
        public decimal? Price { get; set; }
        public string? Currency { get; set; }
        public string? Status { get; set; }
        public string? ProductStatus { get; set; }
        public string? SaleType { get; set; }
        public string? PrimaryImageUrl { get; set; }
        public bool IsPurchasable { get; set; }
    }

    private sealed class CatalogSwitchRawResponse
    {
        public bool IsValid { get; set; }
        public Guid ProductId { get; set; }
        public Guid SellerId { get; set; }
        public Guid NewSkuId { get; set; }
        public string? NewSkuName { get; set; }
        public decimal NewUnitPrice { get; set; }
        public bool IsPurchasable { get; set; }
        public int WeightGrams { get; set; }
    }
}
