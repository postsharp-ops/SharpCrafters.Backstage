// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Licensing;
using SharpCrafters.Backstage.Licensing.Registration;
using System;

namespace Metalama.Backstage;

/// <summary>
/// Metalama Free, the edition that Metalama 2025.0 and earlier issued.
/// </summary>
/// <remarks>
/// Still registrable, because a user who is going back to one of those versions has no other way to license it. It is
/// not offered during setup: only a version that is being replaced can consume it.
/// </remarks>
[Obsolete]
internal sealed class MetalamaLegacyFreeEdition : SelfRegisteredEdition
{
    /// <inheritdoc />
    public override string Alias => "free";

    /// <inheritdoc />
    public override string DisplayName => "Metalama Free";

    /// <inheritdoc />
    public override string Description => "Registers the Metalama Free license (for Metalama 2025.0 and earlier).";

    /// <inheritdoc />
    public override string SuccessMessage => "You are now using Metalama Free for Metalama 2025.0 and earlier.";

    /// <inheritdoc />
    public override SelfRegisteredEditionKind Kind => SelfRegisteredEditionKind.LegacyFree;

    /// <inheritdoc />
    /// <remarks>
    /// It never expires, because a version that reads it has no way to renew it.
    /// </remarks>
    public override UnsignedLicense CreateLicense( SelfRegisteredEditionContext context )
        => new( LicenseProduct.MetalamaFree, LicenseType.Community, context.UtcNow );
}
