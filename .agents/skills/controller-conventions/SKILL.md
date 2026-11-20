---
name: controller-conventions
description: Canonical standards for ASP.NET Core thin controllers in CommerceService (port 8003). Covers boundary validation, Commerce.Application shared request/response DTOs, English sentence-case error messages, buyer ownership enforcement, and the reference controller blueprint.
---

# Controller Conventions & Standards in CommerceService

> Scope: CommerceService repository only. Never reference parent workspace paths, root documentation, or sibling service directories.

This skill defines the invariant structural and boundary rules for all REST API controllers in `Commerce.Api`.

Controllers serve strictly as **HTTP Transport Boundary Adapters**. They bind incoming HTTP protocol details, enforce buyer resource ownership, invoke Commerce Application use cases, and translate results into standardized HTTP responses. They must never contain business decisions, direct database queries, or manual error formatting.

---

## 1. When to Use

Consult and follow this skill whenever you:
- Create a new controller or add action methods in `Commerce.Api/Controllers/Public/`.
- Review controller code changes for architectural consistency in CommerceService.
- Bind request parameters (`[FromRoute]`, `[FromQuery]`, `[FromBody]`).
- Design validation placement for incoming client payloads.
- Format outbound responses, pagination metadata, or status codes.

---

## 2. Request and Response DTO Placement (Application Layer Sharing)

To prevent duplicate wrapper classes and maintain clean domain boundaries:

- **Declare DTOs in Commerce.Application**: All Request and Response records are declared directly in `Commerce.Application/Services/{Feature}/{Feature}Requests.cs` and `{Feature}Responses.cs`.
- **Direct Consumption by Controller**: Controllers directly bind these Application-layer request records and return these Application-layer response records.
- **Prohibit Duplicate API DTOs**: Never create redundant DTOs in `Commerce.Api` (e.g. prohibited: creating `ApiCreateOrderRequest` in Api only to map into `CreateOrderRequest` in Application).
- **Domain Entities Stay Private**: Never expose EF Core entities or domain aggregate roots (`Order`, `Cart`, `SubOrder`, `CartItem`) through the controller. Only immutable DTO records and `PagedData<T>` leave the application boundary.

---

## 3. Validation Placement (Boundary Discipline)

Validation is concentrated exclusively at system boundaries:

- **Boundary Validation on Request Records**: Place declarative validation attributes from `System.ComponentModel.DataAnnotations` directly on Request DTO records (`[Required]`, `[Range]`, `[MinLength]`, `[RegularExpression]`).
- **Automatic Model State Rejection**: Invalid inputs are intercepted automatically by ASP.NET Core model validation via `ApiValidationProblemFactory`, producing standardized RFC 9110 ProblemDetails with `errorCode = "VAL_INVALID_INPUT"`.
- **PROHIBIT Manual Validation in Controllers**: Never write ad-hoc validation checks inside controller action bodies (e.g. prohibited: `if (request.Quantity <= 0) return BadRequest(...);`).

---

## 4. Error Message Standards (Descriptive Sentence Case)

When domain exceptions, validation failures, or edge guards trigger errors:

- **100% English Language**: All error messages, failure details, and exception descriptions must be written in English.
- **Sentence Case Format**:
  - Capitalize ONLY the first letter of the sentence.
  - All subsequent words must be lowercase, except technical acronyms and identifiers (`ID`, `OTP`, `JWT`, `URL`, `UUID`, `Email`, `API`, `HTTP`).
  - Prohibit ALL-CAPS messages (e.g. prohibited: `"ORDER NOT FOUND"`, `"CART NOT FOUND"`).
  - Prohibit Title Case (e.g. prohibited: `"Order Not Found"`).
- **Contextual Precision**: The message must precisely describe what condition failed so any developer or client can diagnose the issue immediately.
  - Correct: `"Order was not found for the specified identifier."`
  - Correct: `"Order cannot be cancelled in its current processing status."`
  - Correct: `"Cart item was not found in the active shopping cart."`
- **Error Codes**: Always `SCREAMING_SNAKE_CASE` without arbitrary `ERR_` prefixes (`ORDER_NOT_FOUND`, `CART_NOT_FOUND`, `ORDER_INVALID_STATE`, `VAL_INVALID_INPUT`, `FORBIDDEN`).

---

## 5. Canonical Reference Controller Blueprint

The following structural skeleton represents the standardized pattern for controllers in `Commerce.Api`:

```csharp
namespace Commerce.Api.Controllers;

using System;
using System.Threading;
using System.Threading.Tasks;
using Commerce.Api.Extensions;
using Commerce.Api.Filters;
using Commerce.Application.Common.Models;
using Commerce.Application.Services.{Feature};
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/v1/{plural-resources}")]
[RequireRole]
public class {Feature}Controller : ControllerBase
{
    private readonly I{Feature}Service _service;

    public {Feature}Controller(I{Feature}Service service)
    {
        _service = service;
    }

    /// <summary>Gets a single resource by its unique identifier.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        var result = await _service.GetByIdAsync(id, ct);

        if (!this.EnsureOwnership(result.BuyerId, out var ownershipError))
        {
            return ownershipError;
        }

        return Ok(result);
    }

    /// <summary>Lists resources for a specific buyer with pagination.</summary>
    [HttpGet("buyer/{buyerId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListByBuyer(
        [FromRoute] Guid buyerId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (!this.EnsureOwnership(buyerId, out var ownershipError))
        {
            return ownershipError;
        }

        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        PagedData<{Feature}Dto> result = await _service.ListByBuyerIdAsync(buyerId, page, pageSize, ct);
        return Ok(result);
    }

    /// <summary>Creates a new resource from an Application-layer request record.</summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(
        [FromBody] Create{Feature}Request request,
        CancellationToken ct)
    {
        if (!this.TryGetUserId(out var userId, out var authError))
        {
            return authError;
        }

        var result = await _service.CreateAsync(userId, request, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }
}
```

---

## 6. Resource Ownership Guard Rules

- **Enforce Ownership via Extension**: In controllers managing orders or carts, always invoke `this.EnsureOwnership(targetUserId, out var ownershipError)` before returning sensitive customer records.
- **Reject Spoofed Buyer Identifiers**: Never trust `[FromQuery] Guid buyerId` or `[FromRoute] Guid buyerId` without comparing against the authenticated identity from HttpContext. If unauthorized, immediately return HTTP 403 `FORBIDDEN`.

---

## 7. HTTP Status & Return Conventions

- **GET Single Item**: Return `Task<IActionResult>` with `Ok(dto)` (HTTP 200).
- **GET Paginated List**: Return `Task<IActionResult>` with `Ok(pagedData)` (HTTP 200).
- **POST Creation**: Return `Task<IActionResult>` with `StatusCode(StatusCodes.Status201Created, dto)` (HTTP 201).
- **PUT / PATCH Update**: Return `Task<IActionResult>` with `Ok(dto)` (HTTP 200).
- **DELETE or Empty Operations**: Return `Task<IActionResult>` with `NoContent()` (HTTP 204, 0-byte payload).
- **No Manual Outer Envelopes**: Microservice controllers return pure payloads. Wrapping into `ApiResponse<T>` is performed by Gateway `ResponseEnvelopeMiddleware`.
- **Automatic Error Responses**: All unhandled domain exceptions bubble up to `GlobalExceptionHandlerMiddleware` to format RFC 9110 ProblemDetails.
