namespace Lodestone.Infrastructure.Supporter;

/// <summary>
/// The embedded ECDSA P-256 public key used to verify supporter codes (base64 SPKI). Generate a key
/// pair with <see cref="SupporterCodeIssuer.GenerateKeyPair"/>, keep the private key secret, and paste
/// the public key here. While this is blank, the verifier reports codes as "not configured" and simply
/// no perks can be unlocked — core functionality is unaffected. See docs/SUPPORTERS.md.
/// </summary>
public static class SupporterKeys
{
    public const string DefaultPublicKey = "";
}
