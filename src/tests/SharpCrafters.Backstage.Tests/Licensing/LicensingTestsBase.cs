// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Metalama.Backstage;
using SharpCrafters.Backstage.Application;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Licensing;
using SharpCrafters.Backstage.Licensing.Registration;
using SharpCrafters.Backstage.Testing;
using System;
using System.Linq;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Tests.Licensing
{
    public abstract class LicensingTestsBase : TestsBase
    {
        protected override void OnAfterServicesCreated( Services services )
        {
            base.OnAfterServicesCreated( services );
            this.UserDeviceDetection.IsInteractiveDevice = true;
        }

        protected static TestLicenseKeyProvider LicenseKeyProvider { get; } = new();

        /// <summary>
        /// Gets the catalog of the product family under test.
        /// </summary>
        protected ILicenseProductCatalog Catalog => this.ServiceProvider.GetRequiredBackstageService<ILicenseProductCatalog>();

        /// <summary>
        /// Gets the edition of a given kind that the family under test offers.
        /// </summary>
        /// <exception cref="InvalidOperationException">The family offers no edition of that kind, or more than one.</exception>
        /// <remarks>
        /// The kind and not the alias, because an alias is the verb of a command line and differs from one family to
        /// the next — PostSharp calls its free edition <c>essentials</c> and Metalama calls its own <c>community</c> —
        /// so a test naming one says which family it is testing twice, and a test written against the wrong family
        /// fails by finding nothing rather than by saying so. A family offers at most one edition of each kind.
        /// </remarks>
        protected SelfRegisteredEdition GetEdition( SelfRegisteredEditionKind kind )
            => this.Catalog.SelfRegisteredEditions.Single( e => e.Kind == kind );

        /// <summary>
        /// Registers the edition of a given kind that the family under test offers.
        /// </summary>
        protected LicenseRegistrationResult RegisterEdition( SelfRegisteredEditionKind kind, CommunityLicenseReason reason = CommunityLicenseReason.None )
            => this.LicenseRegistrationService.Register(
                this.GetEdition( kind ),
                new SelfRegisteredEditionOptions { CommunityLicenseReason = reason } );

        /// <summary>
        /// Registers the trial of the family under test.
        /// </summary>
        protected LicenseRegistrationResult RegisterTrial() => this.RegisterEdition( SelfRegisteredEditionKind.Trial );

        /// <summary>
        /// Creates the context that an edition is given, so that a test can ask an edition what it grants without
        /// registering it.
        /// </summary>
        protected SelfRegisteredEditionContext CreateEditionContext( CommunityLicenseReason reason = CommunityLicenseReason.None )
            => new(
                this.ServiceProvider,
                this.LicenseRegistrationService,
                new SelfRegisteredEditionOptions { CommunityLicenseReason = reason } );

        /// <summary>
        /// Gets the version of the test application. A group of license keys whose minimal version is greater than
        /// this version is not read by the services under test.
        /// </summary>
        protected Version CurrentVersion => this.ApplicationInfo.GetLicensingVersion();

        /// <summary>
        /// A version that PostSharp has released, for a test of the PostSharp family. Its keys are stored in groups
        /// named after the versions that can read them, so an application reporting a version below those groups
        /// registers a key and is then told that nothing is registered.
        /// </summary>
        protected const string PostSharpVersion = "2027.0";

        /// <param name="product">
        /// The product family under test. It decides which license keys are consumable at all, because a catalog
        /// rejects a key of another family before any requirement is consulted. The default is Metalama.
        /// </param>
        /// <param name="version">
        /// The version that the test application reports. It decides which groups of license keys the application
        /// reads, because a group named after a later version is not read at all, so a test of a family whose keys
        /// reach a group must report a version that family has actually released. The default is a version below
        /// every group, which is what a test naming its own groups wants.
        /// </param>
        private protected LicensingTestsBase(
            ITestOutputHelper logger,
            bool isTelemetryEnabled = false,
            BackstageProduct? product = null,
            string version = "1.0" ) : base(
            logger,
            new BackstageInitializationOptions(
                new TestApplicationInfo(
                    "Licensing Test App",
                    false,
                    version,
                    new DateTime( 2021, 1, 1, 0, 0, 0, DateTimeKind.Utc ) ) { IsTelemetryEnabled = isTelemetryEnabled },
                product ?? MetalamaProduct.Instance ) { AutoUploadTelemetry = false } ) { }
    }
}