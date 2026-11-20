namespace Commerce.UnitTests.Contracts;

using System;
using System.IO;
using System.Text.Json;
using FluentAssertions;
using Xunit;

/// <summary>
/// Automated Contract Schema Tests verifying inter-service alignment with Catalog Service.
/// Uses the actual OpenAPI / Swagger specification exported by Catalog Service
/// as the Single Source of Truth without spinning up external servers.
/// </summary>
public sealed class CatalogContractSchemaTests
{
    private readonly JsonDocument _swaggerDoc;

    public CatalogContractSchemaTests()
    {
        var contractPath = Path.Combine(AppContext.BaseDirectory, "Contracts", "catalog_swagger.json");
        if (!File.Exists(contractPath))
        {
            contractPath = Path.Combine(Directory.GetCurrentDirectory(), "Contracts", "catalog_swagger.json");
        }
        if (!File.Exists(contractPath))
        {
            contractPath = Path.Combine(Directory.GetCurrentDirectory(), "tests", "Commerce.UnitTests", "Contracts", "catalog_swagger.json");
        }

        File.Exists(contractPath).Should().BeTrue($"Catalog contract file must exist at '{contractPath}'.");

        var jsonContent = File.ReadAllText(contractPath);
        _swaggerDoc = JsonDocument.Parse(jsonContent);
    }

    [Fact]
    public void CatalogApi_MustExpose_BatchVariants_PostEndpoint()
    {
        var paths = _swaggerDoc.RootElement.GetProperty("paths");

        paths.TryGetProperty("/api/v1/internal/variants", out var endpoint)
            .Should().BeTrue("Catalog Service MUST expose route '/api/v1/internal/variants'");

        endpoint.TryGetProperty("post", out var postMethod)
            .Should().BeTrue("Endpoint '/api/v1/internal/variants' MUST support HTTP POST method");

        postMethod.TryGetProperty("summary", out var summary);
        summary.GetString().Should().Contain("Batch gets variant details");
    }

    [Fact]
    public void CatalogApi_BatchVariants_RequestBody_MustAccept_VariantIds()
    {
        var postMethod = _swaggerDoc.RootElement
            .GetProperty("paths")
            .GetProperty("/api/v1/internal/variants")
            .GetProperty("post");

        var content = postMethod.GetProperty("requestBody").GetProperty("content");
        var appJson = content.GetProperty("application/json");

        var schemaRef = appJson.GetProperty("schema").GetProperty("$ref").GetString();
        schemaRef.Should().Be("#/components/schemas/BatchGetVariantsRequest");

        var schemas = _swaggerDoc.RootElement.GetProperty("components").GetProperty("schemas");
        var requestSchema = schemas.GetProperty("BatchGetVariantsRequest");
        var properties = requestSchema.GetProperty("properties");

        properties.TryGetProperty("variantIds", out var variantIdsProp)
            .Should().BeTrue("BatchGetVariantsRequest MUST contain 'variantIds' array property");

        variantIdsProp.GetProperty("type").GetString().Should().Be("array");
    }

    [Fact]
    public void CatalogApi_BatchVariants_Response_MustContain_RequiredCommerceFields()
    {
        var postMethod = _swaggerDoc.RootElement
            .GetProperty("paths")
            .GetProperty("/api/v1/internal/variants")
            .GetProperty("post");

        var responses = postMethod.GetProperty("responses");
        responses.TryGetProperty("200", out var response200)
            .Should().BeTrue("Endpoint MUST return HTTP 200 OK");

        var schemaRef = response200.GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("schema")
            .GetProperty("$ref")
            .GetString();

        schemaRef.Should().Be("#/components/schemas/BatchVariantResponse");

        var schemas = _swaggerDoc.RootElement.GetProperty("components").GetProperty("schemas");
        var responseSchema = schemas.GetProperty("BatchVariantResponse");
        responseSchema.GetProperty("properties").TryGetProperty("items", out var itemsProp)
            .Should().BeTrue("BatchVariantResponse MUST contain 'items' array");

        var itemDtoRef = itemsProp.GetProperty("items").GetProperty("$ref").GetString();
        itemDtoRef.Should().Be("#/components/schemas/BatchVariantItemDto");

        var itemDtoSchema = schemas.GetProperty("BatchVariantItemDto");
        var itemProperties = itemDtoSchema.GetProperty("properties");

        // Verify all fields required by Commerce CatalogClient are present in Catalog's OpenAPI schema
        var requiredFields = new[]
        {
            "variantId",
            "productId",
            "sellerId",
            "productName",
            "price",
            "status",
            "productStatus",
            "isPurchasable"
        };

        foreach (var field in requiredFields)
        {
            itemProperties.TryGetProperty(field, out _)
                .Should().BeTrue($"Catalog BatchVariantItemDto MUST define property '{field}' to satisfy Commerce contract.");
        }
    }

    [Fact]
    public void CatalogApi_MustExpose_ValidateVariantSwitch_EndpointAndSchema()
    {
        var paths = _swaggerDoc.RootElement.GetProperty("paths");

        paths.TryGetProperty("/api/v1/internal/catalog/skus/validate-variant-switch", out var endpoint)
            .Should().BeTrue("Catalog Service MUST expose route '/api/v1/internal/catalog/skus/validate-variant-switch'");

        endpoint.TryGetProperty("post", out var postMethod)
            .Should().BeTrue("Validate variant switch endpoint MUST support HTTP POST");

        var schemas = _swaggerDoc.RootElement.GetProperty("components").GetProperty("schemas");

        // Verify Request Schema
        var reqSchema = schemas.GetProperty("ValidateVariantSwitchRequest").GetProperty("properties");
        reqSchema.TryGetProperty("currentSkuId", out _).Should().BeTrue("Request MUST contain 'currentSkuId'");
        reqSchema.TryGetProperty("newSkuId", out _).Should().BeTrue("Request MUST contain 'newSkuId'");

        // Verify Response Schema
        var resSchema = schemas.GetProperty("ValidateVariantSwitchResponse").GetProperty("properties");
        resSchema.TryGetProperty("isValid", out _).Should().BeTrue("Response MUST contain 'isValid'");
        resSchema.TryGetProperty("newSkuId", out _).Should().BeTrue("Response MUST contain 'newSkuId'");
        resSchema.TryGetProperty("newUnitPrice", out _).Should().BeTrue("Response MUST contain 'newUnitPrice'");
        resSchema.TryGetProperty("isPurchasable", out _).Should().BeTrue("Response MUST contain 'isPurchasable'");
    }

    [Fact]
    public void CatalogApi_MustExpose_SingleVariant_Endpoints()
    {
        var paths = _swaggerDoc.RootElement.GetProperty("paths");

        // Canonical route
        paths.TryGetProperty("/api/v1/internal/variants/{id}", out var endpoint1)
            .Should().BeTrue("Catalog Service MUST expose single variant lookup '/api/v1/internal/variants/{id}'");
        endpoint1.TryGetProperty("get", out _).Should().BeTrue("Route must support HTTP GET");

        // Alias route for backward compatibility
        paths.TryGetProperty("/api/v1/internal/catalog/skus/{id}", out var endpoint2)
            .Should().BeTrue("Catalog Service MUST expose single variant alias '/api/v1/internal/catalog/skus/{id}'");
        endpoint2.TryGetProperty("get", out _).Should().BeTrue("Alias route must support HTTP GET");
    }

    [Fact]
    public void CatalogApi_MustExpose_BatchCheckStatus_Endpoint()
    {
        var paths = _swaggerDoc.RootElement.GetProperty("paths");

        paths.TryGetProperty("/api/v1/internal/variants/check-status", out var endpoint)
            .Should().BeTrue("Catalog Service MUST expose purchasability check '/api/v1/internal/variants/check-status'");
        endpoint.TryGetProperty("post", out _).Should().BeTrue("Check-status route must support HTTP POST");

        var schemas = _swaggerDoc.RootElement.GetProperty("components").GetProperty("schemas");
        schemas.TryGetProperty("CheckVariantStatusRequest", out _).Should().BeTrue();
        schemas.TryGetProperty("CheckVariantStatusResponse", out _).Should().BeTrue();
    }

    [Fact]
    public void CatalogApi_MustExpose_AlternativeAliases_ForCompatibility()
    {
        var paths = _swaggerDoc.RootElement.GetProperty("paths");

        // Batch alias
        paths.TryGetProperty("/api/v1/internal/catalog/skus:batch-get", out var batchAlias)
            .Should().BeTrue("Catalog Service MUST support batch alias '/api/v1/internal/catalog/skus:batch-get'");
        batchAlias.TryGetProperty("post", out _).Should().BeTrue();

        // Validate switch alias
        paths.TryGetProperty("/api/v1/internal/variants/validate-switch", out var switchAlias)
            .Should().BeTrue("Catalog Service MUST support switch alias '/api/v1/internal/variants/validate-switch'");
        switchAlias.TryGetProperty("post", out _).Should().BeTrue();
    }
}
