using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using Nexora.Features.Updates.Infrastructure;
using Xunit;

namespace Nexora.Tests.Features.Updates;

/// <summary>
/// Hermetic regression tests for the publisher-identity predicate: crafted
/// in-memory self-signed certificates pin that only the ASN.1 Common Name
/// decides, never another attribute or a rendered-string substring. No store,
/// network, or filesystem is touched; every certificate is disposed.
/// </summary>
public sealed class PublisherIdentityTests
{
    private const string ExpectedPublisher = "Nexora";

    [Fact]
    public void TryGetCommonName_ReturnsNull_ForOrganizationWithoutCommonName()
    {
        // THE attack: a certificate whose Organization names the publisher but
        // carries no Common Name must not satisfy the publisher check.
        using var cert = CreateSelfSigned("O=Nexora");

        PublisherIdentity.TryGetCommonName(cert).Should().BeNull(
            "no id-at-commonName attribute exists in this subject");
        PublisherIdentity.IsExpectedPublisher(cert, ExpectedPublisher).Should().BeFalse(
            "an O attribute must never be mistaken for the CN type");
    }

    [Fact]
    public void IsExpectedPublisher_AcceptsExactCommonName()
    {
        using var cert = CreateSelfSigned("CN=Nexora");

        PublisherIdentity.TryGetCommonName(cert).Should().Be("Nexora");
        PublisherIdentity.IsExpectedPublisher(cert, ExpectedPublisher).Should().BeTrue();
    }

    [Fact]
    public void IsExpectedPublisher_IgnoresCommonNameCase()
    {
        using var cert = CreateSelfSigned("CN=NEXORA");

        PublisherIdentity.IsExpectedPublisher(cert, ExpectedPublisher).Should().BeTrue(
            "the comparison is OrdinalIgnoreCase by design");
    }

    [Fact]
    public void IsExpectedPublisher_RejectsCommonNamePrefix()
    {
        using var cert = CreateSelfSigned("CN=Nexor");

        PublisherIdentity.IsExpectedPublisher(cert, ExpectedPublisher).Should().BeFalse(
            "the comparison is anchored equality, never a prefix or substring match");
    }

    [Fact]
    public void TryGetCommonName_ReturnsFullValue_ForCommaBearingCommonName()
    {
        // Windows renders this subject quoted (CN="Nexora, LLC"); the extractor
        // must return the raw value untruncated, or the trailing " LLC" would
        // be lost and a truncated "Nexora" could wrongly match.
        using var cert = CreateSelfSigned("CN=\"Nexora, LLC\"");

        PublisherIdentity.TryGetCommonName(cert).Should().Be("Nexora, LLC");
        PublisherIdentity.IsExpectedPublisher(cert, ExpectedPublisher).Should().BeFalse(
            "the full value is not equal to the expected publisher");
    }

    /// <summary>
    /// Builds an in-memory self-signed certificate with the given subject. The
    /// certificate is never added to a store; the key and certificate are
    /// released when the caller disposes the return value (and this RSA).
    /// </summary>
    private static X509Certificate2 CreateSelfSigned(string subject)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
        return request.CreateSelfSigned(notBefore, notBefore.AddDays(30));
    }
}
