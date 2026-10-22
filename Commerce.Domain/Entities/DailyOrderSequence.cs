using Commerce.Domain.Base;
using Commerce.Domain.Enums;

namespace Commerce.Domain.Entities;

public class DailyOrderSequence : IAuditable
{
    public DateOnly SeqDate { get; set; }
    public required SequenceType SeqType { get; set; }
    public long LastValue { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
