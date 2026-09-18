// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Telemetry;
using System;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace SharpCrafters.Backstage.Tests.Telemetry;

/// <summary>
/// Tests that the public key which seals a telemetry package is there and is usable.
/// </summary>
/// <remarks>
/// The key is an embedded resource looked up by a name written as a string, so nothing at compile time connects the
/// name to the file. Renaming the file, moving it between projects or changing the root namespace of the assembly
/// leaves the product building and the lookup failing, on a user's machine, while telemetry is being uploaded. These
/// tests are what connects the two.
/// </remarks>
public sealed class TelemetryEncryptionKeyTests
{
    [Fact]
    public void ThePublicKeyIsEmbeddedInTheAssembly()
    {
        var publicKey = TelemetryEncryptionKey.GetPublicKey();

        Assert.NotEmpty( publicKey );
    }

    /// <summary>
    /// The key is the XML of an RSA public key, which is what the uploader hands to the algorithm.
    /// </summary>
    [Fact]
    public void ThePublicKeyIsAnRsaPublicKey()
    {
        var publicKey = Encoding.UTF8.GetString( TelemetryEncryptionKey.GetPublicKey() );

        Assert.Contains( "<RSAKeyValue>", publicKey, StringComparison.Ordinal );
        Assert.Contains( "<Modulus>", publicKey, StringComparison.Ordinal );
        Assert.Contains( "<Exponent>", publicKey, StringComparison.Ordinal );

        // A public key and not a private one: a private key shipped in the product would let anyone read every
        // package every user has ever uploaded.
        Assert.DoesNotContain( "<D>", publicKey, StringComparison.Ordinal );
        Assert.DoesNotContain( "<P>", publicKey, StringComparison.Ordinal );
        Assert.DoesNotContain( "<InverseQ>", publicKey, StringComparison.Ordinal );
    }

    /// <summary>
    /// The algorithm accepts the key and encrypts with it, which is the only thing that proves the resource is a key
    /// rather than a well-formed piece of XML.
    /// </summary>
    [Fact]
    public void ThePublicKeyEncrypts()
    {
        using var rsa = RSA.Create();
        rsa.FromXmlString( Encoding.UTF8.GetString( TelemetryEncryptionKey.GetPublicKey() ) );

        var package = rsa.Encrypt( [1, 2, 3, 4], RSAEncryptionPadding.Pkcs1 );

        Assert.NotEmpty( package );

        // And the product cannot open what it sealed, because only the server holds the other half.
        Assert.Throws<CryptographicException>( () => rsa.Decrypt( package, RSAEncryptionPadding.Pkcs1 ) );
    }

    /// <summary>
    /// Every call returns the same key, and returns a copy of it, so that a caller who writes into what it is given
    /// does not change what the next caller receives.
    /// </summary>
    [Fact]
    public void EveryCallReturnsAnEqualButSeparateCopy()
    {
        var first = TelemetryEncryptionKey.GetPublicKey();
        var second = TelemetryEncryptionKey.GetPublicKey();

        Assert.Equal( first, second );
        Assert.NotSame( first, second );

        first[0] = (byte) (first[0] ^ 0xFF);

        Assert.Equal( second, TelemetryEncryptionKey.GetPublicKey() );
    }
}
