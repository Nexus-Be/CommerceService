namespace Commerce.Application.Services.Order;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Commerce.Application.Interfaces;
using Commerce.Domain.Entities;
using Commerce.Domain.Enums;

/// <summary>
/// Applies the wallet's answer to a refund whose decision was already recorded (status <see cref="RefundStatus.WalletPending"/>).
/// One place decides when a refund counts as done, so every path (cancellation, rejection, return, dispute, approval and the retry
/// worker) behaves the same.
/// </summary>
public static class RefundOutcome
{
    public const string AlreadyHeldByAnotherRefund = "WALLET_REFUND_ALREADY_HELD_BY_ANOTHER_REFUND";

    /// <summary>
    /// Marks the refund <see cref="RefundStatus.Completed"/> and stores the wallet transaction only when the wallet really made, or had
    /// already made for this very refund, the credit. Returns false when it did not answer, and when the transaction it returned
    /// already belongs to a different refund of the same order: then this refund moved no money and is not marked as refunded.
    /// </summary>
    public static async Task<bool> ApplyAsync(ICommerceDbContext db, OrderRefund refund, WalletRefundResult result, CancellationToken cancellationToken)
    {
        if (!result.Succeeded) return false;

        if (result.Replayed && result.TransactionId is { } existingTransaction)
        {
            var heldByAnother = await db.OrderRefunds.AnyAsync(
                other => other.Id != refund.Id && other.RefundTransactionId == existingTransaction, cancellationToken);
            if (heldByAnother)
            {
                refund.Status = RefundStatus.Failed;
                refund.FailureCode = AlreadyHeldByAnotherRefund;
                refund.FailedAt = DateTimeOffset.UtcNow;
                return false;
            }
        }

        refund.Status = RefundStatus.Completed;
        refund.CompletedAt = DateTimeOffset.UtcNow;
        refund.RefundTransactionId = result.TransactionId;
        refund.FailureCode = null;
        return true;
    }
}
