namespace QuickShare.Infrastructure.Constants;

/// <summary>
/// Which rail a payment is taken on (issue #225).
///
/// The plan, the price and the invoice are identical whichever it is — only the checkout page
/// and the confirmation differ. Anything that branches on more than that is a sign the second
/// provider is being modelled as a second product.
/// </summary>
public static class PaymentProviders
{
    public const string PayOS = "payos";
    public const string Crypto = "crypto";

    public static readonly string[] All = [PayOS, Crypto];
}
