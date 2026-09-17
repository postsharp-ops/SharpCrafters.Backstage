// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Licensing.LicenseServer;
using System;
using Xunit;

namespace SharpCrafters.Backstage.Tests.Licensing.LicenseServer;

/// <summary>
/// Tests how the product tells the address of a license server apart from a licence key. A user registers one or
/// the other with the same command, so this is what decides whether their build reads a key or asks their
/// organization for a seat.
/// </summary>
public sealed class LicenseServerUrlTests
{
    /// <summary>
    /// Tests the shapes of URL that an administrator may reasonably give when they register their license server:
    /// with or without a trailing slash, with a port, with a path, and by address rather than by name. Refusing any
    /// of them would send the administrator looking for a fault in a server that is working.
    /// </summary>
    [Theory]
    [InlineData( "http://license.test" )]
    [InlineData( "https://license.test" )]
    [InlineData( "https://license.test/" )]
    [InlineData( "https://license.test:8443" )]
    [InlineData( "https://license.test/postsharp" )]
    [InlineData( "https://license.test/postsharp/" )]
    [InlineData( "https://192.168.1.10:8080/licenses" )]
    public void WellFormedUrlIsRecognized( string url )
    {
        Assert.True( LicenseServerUrl.IsLicenseServerUrl( url, out var errorMessage ) );
        Assert.Null( errorMessage );
    }

    /// <summary>
    /// Tests that a license key is not mistaken for a license server URL. This is the dispatch that decides whether a
    /// registered license string is deserialized as a key or leased from a server.
    /// </summary>
    [Theory]
    [InlineData( "3-ZEQQQQQQZTQEQCRCE4UW3UFEB4URXMHRB8KQBJJSB64LX7EAEJZWKEM8SCXJK6KJLFD92CAJFQKCGC67A9NVYA2JGNEHLB8QQG4JAF94J58KUJQZW8ZQQDTFJJPA" )]
    [InlineData( "license.test" )]
    [InlineData( "SomeInvalidLicenseString" )]
    [InlineData( "" )]
    [InlineData( " " )]
    [InlineData( null )]
    public void NonUrlIsNotRecognized( string? licenseString )
    {
        Assert.False( LicenseServerUrl.IsLicenseServerUrl( licenseString, out var errorMessage ) );
        Assert.Equal( "Invalid URL.", errorMessage );
    }

    /// <summary>
    /// Tests that a URL the product cannot fetch a lease from is refused at once, with the reason. The lease is
    /// requested over HTTP, so a URL of any other protocol can never work, and saying so while the administrator is
    /// still typing is worth more than failing on their next build.
    /// </summary>
    [Theory]
    [InlineData( "ftp://license.test" )]
    [InlineData( "file:///c:/licenses" )]
    [InlineData( "net.tcp://license.test" )]
    public void UnsupportedSchemeIsRejected( string url )
    {
        Assert.False( LicenseServerUrl.IsLicenseServerUrl( url, out var errorMessage ) );
        Assert.Equal( "Only HTTP and HTTPS are acceptable protocols.", errorMessage );
    }

    /// <summary>
    /// Tests that an address which already carries arguments is refused. The product adds its own when it asks for
    /// a lease, so such an address would produce a request the server cannot read, and the user would see a failure
    /// that names their server rather than what they typed.
    /// </summary>
    [Theory]
    [InlineData( "https://license.test?user=x" )]
    [InlineData( "https://license.test/Lease.ashx?user=x&machine=y" )]
    public void QueryStringIsRejected( string url )
    {
        Assert.False( LicenseServerUrl.IsLicenseServerUrl( url, out var errorMessage ) );
        Assert.Equal( "The URL cannot contain a query string.", errorMessage );
    }

    /// <summary>
    /// Tests that a URL carrying a user name or a password is rejected. PostSharp accepted one because
    /// <c>WebClient</c> honoured it; <c>HttpClient</c> ignores it, so accepting one would send an anonymous request
    /// while the user believes a credential was configured, and would keep that credential in clear text.
    /// </summary>
    [Theory]
    [InlineData( "https://alice@license.test" )]
    [InlineData( "https://alice:secret@license.test" )]
    public void UserInfoIsRejected( string url )
    {
        Assert.False( LicenseServerUrl.IsLicenseServerUrl( url, out var errorMessage ) );
        Assert.Contains( "cannot contain a user name", errorMessage, StringComparison.Ordinal );
    }

    /// <summary>
    /// Tests that the key of the lease store is the same for two registrations that differ only by a trailing slash,
    /// so that they share one lease instead of leasing a seat each.
    /// </summary>
    [Theory]
    [InlineData( "https://license.test", "https://license.test" )]
    [InlineData( "https://license.test/", "https://license.test" )]
    [InlineData( "https://license.test///", "https://license.test" )]
    [InlineData( "https://license.test/postsharp/", "https://license.test/postsharp" )]
    public void StoreKeyIgnoresTrailingSlash( string url, string expectedKey )
    {
        Assert.Equal( expectedKey, LicenseServerUrl.GetStoreKey( url ) );
    }
}
