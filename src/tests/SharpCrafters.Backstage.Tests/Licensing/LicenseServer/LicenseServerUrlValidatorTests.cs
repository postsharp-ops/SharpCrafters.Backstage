// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Configuration;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Licensing.LicenseServer;
using System;
using Xunit;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Tests.Licensing.LicenseServer;

/// <summary>
/// Tests the service that decides whether the URL of a license server may be used, which is the single place that
/// answers that question for the license factory, the registration service and the commands.
/// </summary>
public sealed class LicenseServerUrlValidatorTests : LicenseServerTestsBase
{
    public LicenseServerUrlValidatorTests( ITestOutputHelper logger ) : base( logger ) { }

    private LicenseServerUrlValidator Validator
    {
        get
        {
            this.EnsureServicesInitialized();

            return this.ServiceProvider.GetRequiredBackstageService<LicenseServerUrlValidator>();
        }
    }

    /// <summary>
    /// Tests that an HTTPS server is accepted and says nothing. The great majority of servers are of this kind, and
    /// a warning they cannot act upon is a warning users learn to ignore, including the ones that matter.
    /// </summary>
    [Theory]
    [InlineData( "https://license.test" )]
    [InlineData( "https://license.test/" )]
    [InlineData( "https://license.test:8443/postsharp" )]
    public void SecureUrlIsValidAndSilent( string url )
    {
        Assert.True( this.Validator.TryValidate( url, out var errorMessage, out var warning ) );

        Assert.Null( errorMessage );
        Assert.Null( warning );
    }

    /// <summary>
    /// Tests that a URL the product cannot use is refused with the reason it cannot be used. The administrator who
    /// typed it is the only person who can correct it, and they can only do so if they are told what is wrong
    /// rather than that something is.
    /// </summary>
    [Theory]
    [InlineData( "https://license.test?user=x", "query string" )]
    [InlineData( "ftp://license.test", "HTTP and HTTPS" )]
    [InlineData( "file:///c:/licenses", "HTTP and HTTPS" )]
    [InlineData( "https://alice:secret@license.test", "user name" )]
    [InlineData( "not a url", "Invalid URL" )]
    [InlineData( null, "Invalid URL" )]
    public void MalformedUrlIsRefusedWithItsReason( string? url, string expectedMessageSubstring )
    {
        Assert.False( this.Validator.TryValidate( url, out var errorMessage, out var warning ) );

        Assert.Contains( expectedMessageSubstring, errorMessage, StringComparison.Ordinal );

        // A URL that is refused deserves no warning: the error already says everything the user can act upon.
        Assert.Null( warning );
    }

    /// <summary>
    /// Tests that an insecure URL is valid and warned about, which is the whole shape of this policy: it is never a
    /// reason to refuse a server.
    /// </summary>
    [Theory]
    [InlineData( "http://license.test" )]
    [InlineData( "http://localhost:8080" )]
    public void InsecureUrlIsValidAndWarned( string url )
    {
        Assert.True( this.Validator.TryValidate( url, out var errorMessage, out var warning ) );

        Assert.Null( errorMessage );
        Assert.Contains( "cleartext", warning, StringComparison.Ordinal );
        Assert.Contains( "allowInsecureLicenseServer", warning, StringComparison.Ordinal );
    }

    /// <summary>
    /// Tests that the warning can be turned off. An organization that runs its license server on a network it
    /// controls has made its decision, and the product must stop repeating the question on every build.
    /// </summary>
    [Fact]
    public void InsecureUrlIsSilentOnceAllowed()
    {
        this.ConfigurationManager!.Update<SharpCrafters.Backstage.Licensing.LicensingConfiguration>(
            configuration => configuration with { AllowInsecureLicenseServer = true } );

        Assert.True( this.Validator.TryValidate( "http://license.test", out var errorMessage, out var warning ) );

        Assert.Null( errorMessage );
        Assert.Null( warning );
    }

    /// <summary>
    /// Tests that allowing an insecure server does not make a malformed URL acceptable. The setting silences a
    /// warning; it is not a way of turning the validation off.
    /// </summary>
    [Fact]
    public void AllowingInsecureUrlsDoesNotAcceptAMalformedOne()
    {
        this.ConfigurationManager!.Update<SharpCrafters.Backstage.Licensing.LicensingConfiguration>(
            configuration => configuration with { AllowInsecureLicenseServer = true } );

        Assert.False( this.Validator.TryValidate( "http://license.test?user=x", out var errorMessage, out _ ) );

        Assert.Contains( "query string", errorMessage, StringComparison.Ordinal );
    }
}
