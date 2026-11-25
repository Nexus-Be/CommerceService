namespace Commerce.Application.Services.RefundFund;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Commerce.Application.Common.Models;
using Commerce.Application.Interfaces;
using Commerce.Domain.Entities;
using Commerce.Domain.Enums;
using Commerce.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

public sealed record RefundFundCaseResponse(
    Guid Id,
    Guid SubOrderId,
    Guid ParentOrderId,
    Guid BuyerId,
    Guid SellerId,
    decimal Amount,
    string Status,
    string? NeedsFinanceReason,
    Guid FirstApproverId,
    Guid? SecondApproverId,
    decimal? SellerRecovered,
    decimal? SellerShortfall,
    Guid? BuyerTransactionId,
    int Attempts,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RefundedAt,
    Guid? ResolvedBy,
    string? ResolutionNote,
    bool AlreadyExisted);

/// <summary>
/// T30 phase 1: refunds a buyer from the Wallet's refund fund after the sub-order was already paid out to the seller. The decision and the
/// approvals are stored first, then the Wallet is asked; the sub-order is shown as refunded only after the Wallet confirms. A case the fund
/// cannot pay waits in NeedsFinance for a person. One case per sub-order (unique), so two reports reach the same case. Every change of status
/// is a conditional update that names the status it expects.
/// </summary>
public sealed class RefundFundService
{
    public const int MinTextLength = 10;
    public const decimal DefaultSecondApprovalThreshold = 200_000m;

    private readonly ICommerceDbContext _db;
    private readonly IWalletClient _wallet;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RefundFundService> _logger;

    public RefundFundService(ICommerceDbContext db, IWalletClient wallet, IConfiguration configuration, ILogger<RefundFundService> logger)
    {
        _db = db;
        _wallet = wallet;
        _configuration = configuration;
        _logger = logger;
    }

    private decimal Threshold => decimal.TryParse(_configuration["RefundFund:SecondApprovalThreshold"], System.Globalization.NumberStyles.Number,
        System.Globalization.CultureInfo.InvariantCulture, out var value) && value > 0 ? value : DefaultSecondApprovalThreshold;

    /// <summary>An admin decides to refund the sub-order from the fund. Safe to repeat: the existing case is returned.</summary>
    public async Task<RefundFundCaseResponse> RequestAsync(Guid subOrderId, Guid adminId, string? reason, Guid? reportId, CancellationToken ct)
    {
        var text = ValidateText(reason, "reason");
        RequireAdmin(adminId);

        var existing = await _db.RefundFundCases.AsNoTracking().FirstOrDefaultAsync(item => item.SubOrderId == subOrderId, ct);
        if (existing is not null)
        {
            return await ContinueAsync(existing, alreadyExisted: true, ct);
        }

        var sub = await _db.SubOrders.AsNoTracking().FirstOrDefaultAsync(item => item.Id == subOrderId, ct)
            ?? throw new CommerceDomainException("SUB_ORDER_NOT_FOUND", "Sub-order not found.", 404);
        if (sub.PaymentMethod != PaymentMethod.Wallet)
            throw new CommerceDomainException("REFUND_FUND_WALLET_ONLY", "Only a sub-order paid by wallet can be refunded from the fund.", 409);
        if (!sub.IsEscrowSettled)
            throw new CommerceDomainException("SUB_ORDER_NOT_PAID_OUT", "The sub-order has not been paid out to the seller; refund it from its escrow instead.", 409);
        if (sub.Status is SubOrderStatus.Refunded or SubOrderStatus.Cancelled or SubOrderStatus.PendingPayment)
            throw new CommerceDomainException("SUB_ORDER_NOT_REFUNDABLE", "The sub-order is not in a state that can be refunded from the fund.", 409);
        var other = await _db.OrderRefunds.AsNoTracking().AnyAsync(item => item.SubOrderId == subOrderId
            && (item.Status == RefundStatus.Completed || item.Status == RefundStatus.Requested || item.Status == RefundStatus.WalletPending), ct);
        if (other)
            throw new CommerceDomainException("REFUND_ALREADY_EXISTS", "The sub-order already has a refund or a refund in progress.", 409);

        var amount = sub.TotalAmount;
        var created = new RefundFundCase
        {
            Id = Guid.NewGuid(),
            SubOrderId = subOrderId,
            ParentOrderId = sub.OrderId,
            BuyerId = sub.BuyerId,
            SellerId = sub.SellerId,
            Amount = amount,
            ReportId = reportId,
            RequestReason = text,
            FirstApproverId = adminId,
            Status = amount > Threshold ? RefundFundCaseStatus.AwaitingSecondApproval : RefundFundCaseStatus.Pending
        };
        _db.RefundFundCases.Add(created);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Another request created the case for the same sub-order first: that one is the case.
            var winner = await _db.RefundFundCases.AsNoTracking().FirstOrDefaultAsync(item => item.SubOrderId == subOrderId, ct);
            if (winner is null) throw;
            return ToResponse(winner, alreadyExisted: true);
        }

        return await ContinueAsync(created, alreadyExisted: false, ct);
    }

    /// <summary>The second admin approves a case above the threshold. It must be a different admin; a repeat changes nothing.</summary>
    public async Task<RefundFundCaseResponse> ApproveSecondAsync(Guid subOrderId, Guid adminId, CancellationToken ct)
    {
        RequireAdmin(adminId);
        var current = await FindAsync(subOrderId, ct);
        if (current.Status != RefundFundCaseStatus.AwaitingSecondApproval)
        {
            return await ContinueAsync(current, alreadyExisted: true, ct);
        }

        if (current.FirstApproverId == adminId)
            throw new CommerceDomainException("SECOND_APPROVER_MUST_DIFFER", "The second approval must come from a different admin.", 403);

        await ChangeStatusAsync(subOrderId, RefundFundCaseStatus.AwaitingSecondApproval, RefundFundCaseStatus.Pending, item =>
        {
            item.SecondApproverId = adminId;
        }, ct);
        return await ContinueAsync(await FindAsync(subOrderId, ct), alreadyExisted: false, ct);
    }

    /// <summary>Asks the Wallet again for a case in NeedsFinance (after the fund was topped up, for example).</summary>
    public async Task<RefundFundCaseResponse> RetryAsync(Guid subOrderId, Guid adminId, CancellationToken ct)
    {
        RequireAdmin(adminId);
        var current = await FindAsync(subOrderId, ct);
        if (current.Status == RefundFundCaseStatus.NeedsFinance)
        {
            await ChangeStatusAsync(subOrderId, RefundFundCaseStatus.NeedsFinance, RefundFundCaseStatus.Pending, item => item.NeedsFinanceReason = null, ct);
            current = await FindAsync(subOrderId, ct);
        }

        return await ContinueAsync(current, alreadyExisted: true, ct);
    }

    /// <summary>A person closes a NeedsFinance case after handling it outside the system. No money moves here.</summary>
    public async Task<RefundFundCaseResponse> ResolveAsync(Guid subOrderId, Guid adminId, string? note, CancellationToken ct)
    {
        RequireAdmin(adminId);
        var text = ValidateText(note, "note");
        var current = await FindAsync(subOrderId, ct);
        if (current.Status != RefundFundCaseStatus.NeedsFinance)
        {
            return ToResponse(current, alreadyExisted: true);
        }

        await ChangeStatusAsync(subOrderId, RefundFundCaseStatus.NeedsFinance, RefundFundCaseStatus.Resolved, item =>
        {
            item.ResolvedBy = adminId;
            item.ResolutionNote = text;
            item.ResolvedAt = DateTimeOffset.UtcNow;
        }, ct);
        return ToResponse(await FindAsync(subOrderId, ct), alreadyExisted: false);
    }

    public async Task<PagedData<RefundFundCaseResponse>> ListAsync(RefundFundCaseStatus? status, int page, int pageSize, CancellationToken ct)
    {
        var safePage = Math.Max(1, page);
        var safeSize = Math.Clamp(pageSize, 1, 100);
        var query = _db.RefundFundCases.AsNoTracking();
        if (status is not null) query = query.Where(item => item.Status == status);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(item => item.CreatedAt).Skip((safePage - 1) * safeSize).Take(safeSize).ToListAsync(ct);
        return new PagedData<RefundFundCaseResponse>(items.Select(item => ToResponse(item, alreadyExisted: true)).ToList(), PaginationMetadata.Create(safePage, safeSize, total));
    }

    /// <summary>The worker's pass: repeats the request for every case that was approved but not yet confirmed by the Wallet.</summary>
    public async Task<int> RetryPendingAsync(int batchSize, CancellationToken ct)
    {
        var pending = await _db.RefundFundCases.AsNoTracking()
            .Where(item => item.Status == RefundFundCaseStatus.Pending)
            .OrderBy(item => item.UpdatedAt)
            .Take(batchSize)
            .ToListAsync(ct);
        var done = 0;
        foreach (var item in pending)
        {
            try
            {
                var result = await SendAsync(item, ct);
                if (result.Status is "Refunded" or "NeedsFinance") done++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "[RefundFund] Sending the case of sub-order '{SubOrderId}' failed; it stays Pending.", item.SubOrderId);
            }
        }

        return done;
    }

    // ------------------------------------------------------------------ steps

    private async Task<RefundFundCaseResponse> ContinueAsync(RefundFundCase current, bool alreadyExisted, CancellationToken ct) =>
        current.Status == RefundFundCaseStatus.Pending ? await SendAsync(current, ct, alreadyExisted) : ToResponse(current, alreadyExisted);

    private async Task<RefundFundCaseResponse> SendAsync(RefundFundCase current, CancellationToken ct, bool alreadyExisted = false)
    {
        var response = await _wallet.PayRefundFundCaseAsync(new RefundFundPayRequest(
            current.SubOrderId, current.ParentOrderId, current.BuyerId, current.Amount, current.FirstApproverId, current.SecondApproverId), ct);

        if (response is null)
        {
            // The Wallet could not be reached or is not ready: the decision stays Pending and the worker repeats it.
            await ChangeStatusAsync(current.SubOrderId, RefundFundCaseStatus.Pending, RefundFundCaseStatus.Pending, item => item.Attempts++, ct);
            return ToResponse(await FindAsync(current.SubOrderId, ct), alreadyExisted);
        }

        if (response.Outcome == "Refunded")
        {
            var changed = await ChangeStatusAsync(current.SubOrderId, RefundFundCaseStatus.Pending, RefundFundCaseStatus.Refunded, item =>
            {
                item.Attempts++;
                item.RefundedAt = DateTimeOffset.UtcNow;
                item.SellerRecovered = response.SellerRecovered;
                item.SellerShortfall = response.SellerShortfall;
                item.BuyerTransactionId = Guid.TryParse(response.BuyerTransactionId, out var tx) ? tx : null;
            }, ct);
            if (changed)
            {
                await MarkSubOrderRefundedAsync(current.SubOrderId, ct);
            }
        }
        else
        {
            var reason = response.Outcome == "Rejected" ? $"WALLET_{response.Reason}" : response.Reason ?? "NEEDS_FINANCE";
            await ChangeStatusAsync(current.SubOrderId, RefundFundCaseStatus.Pending, RefundFundCaseStatus.NeedsFinance, item =>
            {
                item.Attempts++;
                item.NeedsFinanceReason = reason.Length > 100 ? reason[..100] : reason;
            }, ct);
        }

        return ToResponse(await FindAsync(current.SubOrderId, ct), alreadyExisted);
    }

    private async Task MarkSubOrderRefundedAsync(Guid subOrderId, CancellationToken ct)
    {
        var sub = await _db.SubOrders.FirstOrDefaultAsync(item => item.Id == subOrderId, ct);
        if (sub is null || sub.Status == SubOrderStatus.Refunded) return;
        sub.Status = SubOrderStatus.Refunded;
        sub.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Changes the status only while it still is <paramref name="from"/>. True when this call made the change.</summary>
    private async Task<bool> ChangeStatusAsync(Guid subOrderId, RefundFundCaseStatus from, RefundFundCaseStatus to, Action<RefundFundCase> apply, CancellationToken ct)
    {
        var tracked = await _db.RefundFundCases.FirstOrDefaultAsync(item => item.SubOrderId == subOrderId, ct);
        if (tracked is null || tracked.Status != from) return false;

        if (_db.Database.IsRelational())
        {
            // The status is part of the update's condition, so two callers cannot both win. Attempts and the other fields are written by the
            // same statement through the tracked copy being compared with the database row.
            var updated = await _db.RefundFundCases
                .Where(item => item.Id == tracked.Id && item.Status == from)
                .ExecuteUpdateAsync(set => set.SetProperty(item => item.Status, to).SetProperty(item => item.UpdatedAt, DateTimeOffset.UtcNow), ct);
            if (updated != 1) return false;
            // The status is already changed in the database; only the other fields are saved through the tracked copy (its Status is untouched).
            apply(tracked);
            await _db.SaveChangesAsync(ct);
            return true;
        }

        tracked.Status = to;
        tracked.UpdatedAt = DateTimeOffset.UtcNow;
        apply(tracked);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<RefundFundCase> FindAsync(Guid subOrderId, CancellationToken ct) =>
        await _db.RefundFundCases.AsNoTracking().FirstOrDefaultAsync(item => item.SubOrderId == subOrderId, ct)
            ?? throw new CommerceDomainException("REFUND_FUND_CASE_NOT_FOUND", "No refund fund case exists for this sub-order.", 404);

    private static void RequireAdmin(Guid adminId)
    {
        if (adminId == Guid.Empty)
            throw new CommerceDomainException("FORBIDDEN", "The admin could not be identified.", 403);
    }

    private static string ValidateText(string? value, string name)
    {
        var text = value?.Trim() ?? string.Empty;
        if (text.Length < MinTextLength || text.Length > 500)
            throw new CommerceDomainException("VAL_INVALID_INPUT", $"The {name} must be between {MinTextLength} and 500 characters long.");
        return text;
    }

    private static RefundFundCaseResponse ToResponse(RefundFundCase item, bool alreadyExisted) => new(
        item.Id, item.SubOrderId, item.ParentOrderId, item.BuyerId, item.SellerId, item.Amount, item.Status.ToString(), item.NeedsFinanceReason,
        item.FirstApproverId, item.SecondApproverId, item.SellerRecovered, item.SellerShortfall, item.BuyerTransactionId, item.Attempts,
        item.CreatedAt, item.RefundedAt, item.ResolvedBy, item.ResolutionNote, alreadyExisted);
}
