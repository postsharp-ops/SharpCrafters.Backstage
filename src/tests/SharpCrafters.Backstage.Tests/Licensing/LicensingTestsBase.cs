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
        /// Gets the edition that the family under test offers under an alias.
        /// </summary>
        /// <exception cref="InvalidOperationException">The family offers no such edition.</exception>
        protected SelfRegisteredEdition GetEdition( string alias )
            => this.Catalog.SelfRegisteredEditions.Single( e => string.Equals( e.Alias, alias, StringComparison.OrdinalIgnoreCase ) );

        /// <summary>
        /// Registers the edition that the family under test offers under an alias.
        /// </summary>
        protected LicenseRegistrationResult RegisterEdition( string alias, CommunityLicenseReason reason = CommunityLicenseReason.None )
            => this.LicenseRegistrationService.Register(
                this.GetEdition( alias ),
                new SelfRegisteredEditionOptions { CommunityLicenseReason = reason } );

        /// <summary>
        /// Registers the trial of the family under test.
        /// </summary>
        protected LicenseRegistrationResult RegisterTrial() => this.RegisterEdition( TrialAlias );

        /// <summary>
        /// The alias under which every family offers its trial.
        /// </summary>
        protected const string TrialAlias = "try";

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

        /// <param name="product">
        /// The product family under test. It decides which license keys are consumable at all, because a catalog
        /// rejects a key of another family before any requirement is consulted. The default is Metalama.
        /// </param>
        private protected LicensingTestsBase( ITestOutputHelper logger, bool isTelemetryEnabled = false, BackstageProduct? product = null ) : base(
            logger,
            new BackstageInitializationOptions(
                new TestApplicationInfo(
                    "Licensing Test App",
                    false,
                    "1.0",
                    new DateTime( 2021, 1, 1, 0, 0, 0, DateTimeKind.Utc ) ) { IsTelemetryEnabled = isTelemetryEnabled },
                product ?? MetalamaProduct.Instance ) { AutoUploadTelemetry = false } ) { }
    }
}