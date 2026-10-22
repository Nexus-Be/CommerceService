using Commerce.Domain.Base;
using Commerce.Domain.Enums;

namespace Commerce.Domain.Entities;

public class SystemSetting : BaseEntity<Guid>
{
    public required string SettingKey { get; set; }
    public required string SettingValue { get; set; }
    public required SettingValueType ValueType { get; set; } = SettingValueType.String;
    public required SettingCategory Category { get; set; }
    public string? Description { get; set; }
    public bool IsEditable { get; set; } = true;
    public Guid? UpdatedBy { get; set; }
}
