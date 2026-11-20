namespace Commerce.Domain.Enums;

using System;

public enum PaymentMethod
{
    Wallet = 0,
    [Obsolete("VNPay payment method is decommissioned. Use Wallet or Cod.", false)]
    VnPay = 1,
    Cod = 2
}
