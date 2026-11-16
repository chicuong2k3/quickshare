namespace Unpoly.Blazor.Shadcn;

/// <summary>QR error correction — maps to QRCode.CorrectLevel (L=1, M=0, Q=3, H=2 in qrcode.js).</summary>
public enum QrCodeErrorCorrection
{
    /// <summary>Level L: about 7% of the code can be damaged and still read. Densest data.</summary>
    Low,
    /// <summary>Level M: about 15% recoverable. The usual default.</summary>
    Medium,
    /// <summary>Level Q: about 25% recoverable.</summary>
    Quartile,
    /// <summary>Level H: about 30% recoverable; room for a logo over the centre.</summary>
    High
}
