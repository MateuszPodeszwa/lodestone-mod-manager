using Lodestone.Application.Supporter;
using Lodestone.Domain.Common;
using Lodestone.Infrastructure.Supporter;

namespace Lodestone.Infrastructure.Tests;

public class SupporterCodeTests
{
    [Fact]
    public void A_freshly_issued_code_verifies_against_its_public_key()
    {
        (string priv, string pub) = SupporterCodeIssuer.GenerateKeyPair();
        string code = SupporterCodeIssuer.Issue(priv, "Supporter", "patron@example.com");

        Result<SupporterEntitlement> result = new SignedSupporterCodeVerifier(pub).Verify(code);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Tier.ShouldBe("Supporter");
        result.Value.Holder.ShouldBe("patron@example.com");
    }

    [Fact]
    public void Issued_expiry_is_carried_through()
    {
        (string priv, string pub) = SupporterCodeIssuer.GenerateKeyPair();
        DateTimeOffset expires = DateTimeOffset.Parse("2027-01-01T00:00:00Z");
        string code = SupporterCodeIssuer.Issue(priv, "Champion", "me", expires);

        SupporterEntitlement entitlement = new SignedSupporterCodeVerifier(pub).Verify(code).Value;

        entitlement.Expires.ShouldBe(expires);
        entitlement.Tier.ShouldBe("Champion");
    }

    [Fact]
    public void A_tampered_code_is_rejected()
    {
        (string priv, string pub) = SupporterCodeIssuer.GenerateKeyPair();
        string code = SupporterCodeIssuer.Issue(priv, "Supporter", "me");
        string tampered = code[..^2] + (code[^1] == 'A' ? "B" : "A");

        new SignedSupporterCodeVerifier(pub).Verify(tampered).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void A_code_from_a_different_key_is_rejected()
    {
        (string priv, _) = SupporterCodeIssuer.GenerateKeyPair();
        (_, string otherPub) = SupporterCodeIssuer.GenerateKeyPair();
        string code = SupporterCodeIssuer.Issue(priv, "Supporter", "me");

        new SignedSupporterCodeVerifier(otherPub).Verify(code).IsFailure.ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("only.onepart.three")]
    public void Malformed_input_is_rejected_gracefully(string code)
    {
        (_, string pub) = SupporterCodeIssuer.GenerateKeyPair();

        new SignedSupporterCodeVerifier(pub).Verify(code).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void A_build_without_a_configured_key_reports_unavailable()
    {
        new SignedSupporterCodeVerifier("").Verify("anything").Error.Code.ShouldBe("supporter.unavailable");
    }
}
