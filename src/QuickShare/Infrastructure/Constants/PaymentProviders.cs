namespace QuickShare.Infrastructure.Constants;

/// <summary>
/// Which rail a payment is taken on (issues #225, #164, #177).
///
/// The plan, the price and the invoice are identical whichever it is — only the checkout page
/// and the confirmation differ. Anything that branches on more than that is a sign the second
/// provider is being modelled as a second product.
/// </summary>
public static class PaymentProviders
{
    public const string PayOS = "payos";
    public const string Crypto = "crypto";
    public const string Stripe = "stripe";
    public const string PayPal = "paypal";

    public static readonly string[] All = [PayOS, Crypto, Stripe, PayPal];
}

/// <summary>
/// Payment method channels supported across VN wallets and international cards (issues #164, #177).
/// </summary>
public static class PaymentMethods
{
    public const string BankQr = "bank_qr";
    public const string MoMo = "momo";
    public const string ZaloPay = "zalopay";
    public const string VisaCard = "visa";
    public const string StripeCard = "stripe_card";
    public const string PayPalWallet = "paypal_wallet";
    public const string CryptoWallet = "crypto_wallet";

    public static readonly string[] All = [BankQr, MoMo, ZaloPay, VisaCard, StripeCard, PayPalWallet, CryptoWallet];

    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return BankQr;
        }

        var v = raw.Trim().ToLowerInvariant();
        return All.Contains(v) ? v : BankQr;
    }

    public static string ProviderForMethod(string method) => method switch
    {
        StripeCard => PaymentProviders.Stripe,
        PayPalWallet => PaymentProviders.PayPal,
        CryptoWallet => PaymentProviders.Crypto,
        _ => PaymentProviders.PayOS,
    };
}
