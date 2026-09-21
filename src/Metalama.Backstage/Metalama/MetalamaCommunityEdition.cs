// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Licensing;
using SharpCrafters.Backstage.Licensing.Registration;

namespace Metalama.Backstage.Metalama;

/// <summary>
/// Metalama Community, which is given on conditions and must be renewed every year.
/// </summary>
internal sealed class MetalamaCommunityEdition : SelfRegisteredEdition
{
    /// <inheritdoc />
    public override string Alias => "community";

    /// <inheritdoc />
    public override string DisplayName => "Metalama Community";

    /// <inheritdoc />
    public override string Description => "Switches to the Metalama Community edition, which is free and must be renewed every year.";

    /// <inheritdoc />
    public override string SuccessMessage => "You are now using Metalama Community.";

    /// <inheritdoc />
    public override bool RequiresReason => true;

    /// <inheritdoc />
    /// <remarks>
    /// The edition is installed by the Visual Studio extension, which asks the question that entitles the user to it.
    /// Nobody registers it by hand, so the command line does not advertise a verb for it.
    /// </remarks>
    public override bool IsAvailableFromCommandLine => false;

    /// <inheritdoc />
    /// <remarks>
    /// Metalama Community must be renewed yearly, which is what limits an edition given away for nothing.
    /// </remarks>
    public override UnsignedLicense CreateLicense( SelfRegisteredEditionContext context )
        => new( LicenseProduct.MetalamaCommunity, LicenseType.Community, context.UtcNow ) { ValidTo = context.UtcNow.AddYears( 1 ) };

    /// <inheritdoc />
    protected override LicensingConfiguration OnRegistering( LicensingConfiguration configuration, SelfRegisteredEditionContext context )
        => configuration with { CommunityLicenseReason = context.CommunityLicenseReason };

    /// <inheritdoc />
    /// <remarks>
    /// The reason is checked here and not in <see cref="SelfRegisteredEdition.GetAvailability"/>, because availability
    /// is asked before the user has chosen anything: an edition that refused itself for want of a reason would never
    /// be offered at all.
    /// </remarks>
    protected override LicenseRegistrationResult Register( SelfRegisteredEditionContext context )
        => context.CommunityLicenseReason == CommunityLicenseReason.None
            ? LicenseRegistrationResult.Failure( $"You must say why you are entitled to {this.DisplayName}." )
            : base.Register( context );
}
