// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Configuration.Registry;
using SharpCrafters.Backstage.Infrastructure;

namespace PostSharp.Backstage.Configuration;

/// <summary>
/// The configuration objects that PostSharp keeps in the Windows registry, which are the ones it shares with
/// PostSharp 2026.0.
/// </summary>
/// <remarks>
/// <para>
/// Everything else stays in a file. A setting is put here because the other version has it and the user would be
/// surprised to answer the same question twice, not because the registry is where PostSharp settings live: a setting
/// this version invented has nothing to share and a file is the better store for it.
/// </para>
/// <para>
/// The toast notifications are deliberately absent. PostSharp 2026.0 has a handful of named settings for the
/// questions it asks, and this version has a snooze and a mute for each kind of notification. The two do not
/// correspond, and a mapping between them would be invented rather than observed. See metalama/Metalama#2038.
/// </para>
/// </remarks>
[PublicAPI]
public static class PostSharpConfigurationSchemas
{
    /// <summary>
    /// Creates the schemas.
    /// </summary>
    /// <param name="dateTimeProvider">The clock. The licensing schema writes the timestamp that tells PostSharp
    /// 2026.0 that the registered licenses have changed, and the license server schema dates a lease whose stored
    /// text carries no start time.</param>
    public static IRegistryConfigurationSchema[] Create( IDateTimeProvider dateTimeProvider )
        =>
        [
            new PostSharpLicensingConfigurationSchema( dateTimeProvider ),
            new PostSharpTelemetryConfigurationSchema(),
            new PostSharpLicenseServerConfigurationSchema( dateTimeProvider ),
            new PostSharpLicenseAuditConfigurationSchema()
        ];
}
