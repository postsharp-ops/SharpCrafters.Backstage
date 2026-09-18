// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using PostSharp.Backstage;
using SharpCrafters.Backstage.Configuration;
using SharpCrafters.Backstage.Licensing;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Tests.Licensing.Registration;

/// <summary>
/// Tests that the licenses of the PostSharp family accumulate as they are registered.
/// </summary>
/// <remarks>
/// The editions and the pattern libraries of PostSharp are complementary, so a user registers several keys one after
/// another and expects to end up holding them all. This was not so: every key was stored in the single slot that
/// holds one, so each registration dropped the one before it, and the co-existence rules of the catalog kept products
/// that nothing could store.
/// </remarks>
public sealed class PostSharpLicenseCoexistenceTests : LicensingTestsBase
{
    public PostSharpLicenseCoexistenceTests( ITestOutputHelper logger )
        : base( logger, product: PostSharpProduct.Instance ) { }

    private async Task RegisterAsync( string licenseKey )
        => Assert.True( (await this.LicenseRegistrationService.RegisterLicenseAsync( licenseKey )).IsSuccess );

    /// <summary>
    /// The keys of two complementary products are both held after the second is registered.
    /// </summary>
    [Fact]
    public async Task RegisteringAPatternLibraryKeepsTheEdition()
    {
        await this.RegisterAsync( LicenseKeyProvider.PostSharpFramework );
        await this.RegisterAsync( LicenseKeyProvider.PostSharpLogging );

        var registered = this.LicenseRegistrationService.RegisteredLicenses.Select( l => l.LicenseString ).ToArray();

        Assert.Equal( 2, registered.Length );
        Assert.Contains( LicenseKeyProvider.PostSharpFramework, registered );
        Assert.Contains( LicenseKeyProvider.PostSharpLogging, registered );
    }

    /// <summary>
    /// Four keys of four complementary products are all held. Two would pass even if the keys were merely alternating
    /// between the two slots.
    /// </summary>
    [Fact]
    public async Task EveryPatternLibraryIsHeldAtOnce()
    {
        var licenseKeys = new[]
        {
            LicenseKeyProvider.PostSharpFramework, LicenseKeyProvider.PostSharpLogging, LicenseKeyProvider.PostSharpMvvm,
            LicenseKeyProvider.PostSharpCaching
        };

        foreach ( var licenseKey in licenseKeys )
        {
            await this.RegisterAsync( licenseKey );
        }

        var registered = this.LicenseRegistrationService.RegisteredLicenses.Select( l => l.LicenseString ).ToArray();

        Assert.Equal( licenseKeys.Length, registered.Length );

        foreach ( var licenseKey in licenseKeys )
        {
            Assert.Contains( licenseKey, registered );
        }
    }

    /// <summary>
    /// Registering the same product twice replaces its key rather than holding two keys of it.
    /// </summary>
    [Fact]
    public async Task RegisteringTheSameProductTwiceReplacesItsKey()
    {
        await this.RegisterAsync( LicenseKeyProvider.PostSharpLogging );
        await this.RegisterAsync( LicenseKeyProvider.PostSharpLogging );

        Assert.Single( this.LicenseRegistrationService.RegisteredLicenses );
    }

    /// <summary>
    /// Registering the free edition twice replaces its key, although the key names PostSharp Ultimate as its product
    /// and is recognized as the Essentials edition only once the license type is read.
    /// </summary>
    /// <remarks>
    /// The free edition is the case where the product written in the key and the product the catalog is asked about
    /// differ, so a comparison of the product as written keeps the previous key instead of replacing it. Each visit
    /// to the setup page registers the edition again, so the keys would pile up.
    /// </remarks>
    [Fact]
    public void RegisteringTheFreeEditionTwiceReplacesItsKey()
    {
        Assert.True( this.LicenseRegistrationService.RegisterFreeEdition().IsSuccess );
        Assert.True( this.LicenseRegistrationService.RegisterFreeEdition().IsSuccess );

        Assert.Single( this.LicenseRegistrationService.RegisteredLicenses );
    }

    /// <summary>
    /// The free edition co-exists with a key of a pattern library, which is the point of storing them in a list.
    /// </summary>
    [Fact]
    public async Task TheFreeEditionCoexistsWithAPatternLibrary()
    {
        Assert.True( this.LicenseRegistrationService.RegisterFreeEdition().IsSuccess );
        await this.RegisterAsync( LicenseKeyProvider.PostSharpLogging );

        Assert.Equal( 2, this.LicenseRegistrationService.RegisteredLicenses.Count() );
    }

    /// <summary>
    /// PostSharp Ultimate covers what the others cover, so registering it replaces them all.
    /// </summary>
    [Fact]
    public async Task RegisteringUltimateReplacesEveryOtherLicense()
    {
        await this.RegisterAsync( LicenseKeyProvider.PostSharpFramework );
        await this.RegisterAsync( LicenseKeyProvider.PostSharpLogging );
        await this.RegisterAsync( LicenseKeyProvider.PostSharpUltimate );

        var registered = this.LicenseRegistrationService.RegisteredLicenses.Single();

        Assert.Equal( LicenseKeyProvider.PostSharpUltimate, registered.LicenseString );
    }

    /// <summary>
    /// A key registered after PostSharp Ultimate co-exists with it, because the products complement each other in
    /// whichever order they are registered.
    /// </summary>
    [Fact]
    public async Task RegisteringAPatternLibraryAfterUltimateKeepsUltimate()
    {
        await this.RegisterAsync( LicenseKeyProvider.PostSharpUltimate );
        await this.RegisterAsync( LicenseKeyProvider.PostSharpLogging );

        var registered = this.LicenseRegistrationService.RegisteredLicenses.Select( l => l.LicenseString ).ToArray();

        Assert.Equal( 2, registered.Length );
        Assert.Contains( LicenseKeyProvider.PostSharpUltimate, registered );
        Assert.Contains( LicenseKeyProvider.PostSharpLogging, registered );
    }

    /// <summary>
    /// The keys go to the list, which is the <c>LicenseKeys</c> sub-key of the registry, and not to the single slot,
    /// which is the root value that PostSharp 2026.0 only ever deletes.
    /// </summary>
    [Fact]
    public async Task TheKeysAreStoredInTheList()
    {
        await this.RegisterAsync( LicenseKeyProvider.PostSharpFramework );

        var configuration = this.ConfigurationManager!.Get<LicensingConfiguration>();

        Assert.Null( configuration.LegacyLicense );
        Assert.Equal( LicenseKeyProvider.PostSharpFramework, Assert.Single( configuration.Licenses ) );
    }
}
