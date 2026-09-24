using System.Formats.Asn1;
using System.Security.Cryptography.X509Certificates;

namespace Nexora.Features.Updates.Infrastructure;

/// <summary>
/// Publisher identity: the Common Name predicate behind
/// <see cref="UpdateArchiveValidator.IsAuthenticodeSigned"/>. Moved here
/// verbatim from <see cref="UpdateArchiveValidator"/>; the binding check and
/// chain build that run before it stay there.
/// </summary>
internal static class PublisherIdentity
{
    /// <summary>
    /// Blank expected-publisher means the chain alone is sufficient evidence.
    /// Otherwise the certificate's Common Name must equal it exactly: an anchored
    /// comparison of the one attribute that names the publisher, never a
    /// substring search over the whole distinguished name.
    /// </summary>
    internal static bool IsExpectedPublisher(X509Certificate2 cert, string? expectedPublisher)
    {
        if (string.IsNullOrWhiteSpace(expectedPublisher)) return true;
        return TryGetCommonName(cert) is { } commonName &&
            commonName.Equals(expectedPublisher, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads the Common Name out of the subject's ASN.1 rather than out of the
    /// rendered string, so neither Windows' quoting of comma-bearing values nor
    /// a reordering of attributes changes what this predicate anchors on.
    /// </summary>
    /// <returns>The CN value, or null when the subject carries no CN at all.</returns>
    internal static string? TryGetCommonName(X509Certificate2 cert)
    {
        try
        {
            // Name ::= SEQUENCE OF SET OF AttributeTypeAndValue.
            var relativeNames = new AsnReader(cert.SubjectName.RawData, AsnEncodingRules.BER).ReadSequence();
            while (relativeNames.HasData)
            {
                if (ReadCommonNameFrom(relativeNames.ReadSetOf()) is { } commonName) return commonName;
            }
            return null;
        }
        catch
        {
            // An unreadable subject has no CN this code can trust, so it matches nothing.
            return null;
        }
    }

    /// <summary>The first id-at-commonName attribute within this relative name, if it has one.</summary>
    private static string? ReadCommonNameFrom(AsnReader attributeSet)
    {
        while (attributeSet.HasData)
        {
            var attribute = attributeSet.ReadSequence();
            // The type is read as a whole OID, so an attacker-supplied value can never
            // be mistaken for the CN type: an "O=CN=Nexora" attribute still has type O.
            if (attribute.ReadObjectIdentifier() == CommonNameOid)
            {
                return DecodeDirectoryString(attribute);
            }
        }
        return null;
    }

    /// <summary>
    /// DirectoryString may be encoded under any of several universal string tags —
    /// Windows emits UTF8String, older CAs PrintableString — so the tag is peeked
    /// rather than assumed. A value this decoder cannot read becomes empty, which
    /// cannot match a non-blank expected publisher.
    /// </summary>
    private static string DecodeDirectoryString(AsnReader value)
    {
        var tag = value.PeekTag();
        return tag.TagClass == TagClass.Universal
            ? value.ReadCharacterString((UniversalTagNumber)tag.TagValue)
            : string.Empty;
    }

    /// <summary>id-at-commonName: the attribute this publisher check anchors on.</summary>
    private const string CommonNameOid = "2.5.4.3";
}
