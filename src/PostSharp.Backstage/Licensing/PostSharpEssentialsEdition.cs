// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Licensing;
using SharpCrafters.Backstage.Licensing.Registration;

namespace PostSharp.Backstage.Licensing;

/// <summary>
/// PostSharp Essentials, which is given to everyone.
/// </summary>
internal sealed class PostSharpEssentialsEdition : SelfRegisteredEdition
{
    /// <inheritdoc />
    public override string Alias => "essentials";

    /// <inheritdoc />
    public override string DisplayName => "PostSharp Essentials";

    /// <inheritdoc />
    public override string Description => "Switches to the PostSharp Essentials edition, which is free for everyone.";

    /// <inheritdoc />
    public override string SuccessMessage => "You are now using PostSharp Essentials.";

    /// <inheritdoc />
    /// <remarks>
    /// PostSharp does nothing without a license, so the edition that costs nothing is the first thing the setup pages
    /// offer.
    /// </remarks>
    public override string? SetupTitle => "Start with PostSharp Essentials";

    /// <inheritdoc />
    /// <remarks>
    /// The key names PostSharp Ultimate and carries <see cref="LicenseType.Community"/>, which is how PostSharp 2026.0
    /// writes the key it generates, and the two versions share the keys registered on the machine. It does not expire:
    /// the edition has always been perpetual, and a version that reads it has no way to renew it.
    /// </remarks>
    public override UnsignedLicense CreateLicense( SelfRegisteredEditionContext context )
        => new( LicenseProduct.PostSharpUltimate, LicenseType.Community, context.UtcNow );
}
