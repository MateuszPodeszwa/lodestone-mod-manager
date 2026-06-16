using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lodestone.Application.Supporter;
using Lodestone.Domain.Common;

namespace Lodestone.Infrastructure.Supporter;

/// <summary>The signed body of a supporter code.</summary>
internal sealed record SupporterCodePayload(
    [property: JsonPropertyName("t")] string Tier,
    [property: JsonPropertyName("h")] string Holder,
    [property: JsonPropertyName("e")] DateTimeOffset? Expires);

/// <summary>
/// Verifies offline supporter codes of the form <c>base64url(payload).base64url(signature)</c> using
/// an embedded ECDSA P-256 public key. No payment processing or network call is involved; the private
/// key never ships. Because the unlocked perks are cosmetic, signature verification is sufficient.
/// </summary>
public sealed class SignedSupporterCodeVerifier : ISupporterCodeVerifier
{
    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.General);

    private readonly string _publicKeyBase64;

    public SignedSupporterCodeVerifier(string publicKeyBase64) => _publicKeyBase64 = publicKeyBase64;

    public Result<SupporterEntitlement> Verify(string code)
    {
        if (string.IsNullOrWhiteSpace(_publicKeyBase64))
        {
            return Result.Failure<SupporterEntitlement>("supporter.unavailable",
                "Supporter codes aren't configured in this build.");
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return Result.Failure<SupporterEntitlement>("supporter.invalid", "Enter your supporter code.");
        }

        string[] parts = code.Trim().Split('.');
        if (parts.Length != 2)
        {
            return Result.Failure<SupporterEntitlement>("supporter.invalid", "That code doesn't look right.");
        }

        byte[] payload;
        byte[] signature;
        try
        {
            payload = Base64Url.DecodeFromChars(parts[0]);
            signature = Base64Url.DecodeFromChars(parts[1]);
        }
        catch (FormatException)
        {
            return Result.Failure<SupporterEntitlement>("supporter.invalid", "That code doesn't look right.");
        }

        try
        {
            using ECDsa ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(_publicKeyBase64), out _);
            if (!ecdsa.VerifyData(payload, signature, HashAlgorithmName.SHA256))
            {
                return Result.Failure<SupporterEntitlement>("supporter.invalid", "This code couldn't be verified.");
            }
        }
        catch (CryptographicException)
        {
            return Result.Failure<SupporterEntitlement>("supporter.invalid", "This code couldn't be verified.");
        }

        try
        {
            SupporterCodePayload? body = JsonSerializer.Deserialize<SupporterCodePayload>(payload, PayloadOptions);
            if (body is null || string.IsNullOrWhiteSpace(body.Tier))
            {
                return Result.Failure<SupporterEntitlement>("supporter.invalid", "This code is missing its tier.");
            }

            string holder = string.IsNullOrWhiteSpace(body.Holder) ? "Supporter" : body.Holder;
            return Result.Success(new SupporterEntitlement(body.Tier, holder, body.Expires));
        }
        catch (JsonException)
        {
            return Result.Failure<SupporterEntitlement>("supporter.invalid", "This code couldn't be read.");
        }
    }
}

/// <summary>
/// Issues supporter codes. Run by the maintainer (offline) with the private key — e.g. via the
/// <c>lodestone</c> CLI after a Patreon pledge. Never bundled with a shipped build.
/// </summary>
public static class SupporterCodeIssuer
{
    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.General);

    /// <summary>Generates a new ECDSA P-256 key pair as base64 (PKCS#8 private, SPKI public).</summary>
    public static (string PrivateKeyBase64, string PublicKeyBase64) GenerateKeyPair()
    {
        using ECDsa ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (
            Convert.ToBase64String(ecdsa.ExportPkcs8PrivateKey()),
            Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo()));
    }

    /// <summary>Signs and encodes a redeemable code for a tier/holder, optionally with an expiry.</summary>
    public static string Issue(string privateKeyBase64, string tier, string holder, DateTimeOffset? expires = null)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(
            new SupporterCodePayload(tier, holder, expires), PayloadOptions);

        using ECDsa ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(Convert.FromBase64String(privateKeyBase64), out _);
        byte[] signature = ecdsa.SignData(payload, HashAlgorithmName.SHA256);

        return $"{Base64Url.EncodeToString(payload)}.{Base64Url.EncodeToString(signature)}";
    }
}
