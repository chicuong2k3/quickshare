namespace QuickShare.Features.Protocols;

/// <summary>
/// The body did not match what the request promised about it (issues #265, #195).
///
/// Distinct from a storage failure because it is the caller's problem, not ours: the write is
/// abandoned and the protocol layer turns it into the refusal its clients understand. It lives
/// beside the filesystem rather than inside one protocol so that the write path can rethrow it
/// without knowing which dialect asked.
/// </summary>
public sealed class MountPayloadException(string message) : Exception(message);
