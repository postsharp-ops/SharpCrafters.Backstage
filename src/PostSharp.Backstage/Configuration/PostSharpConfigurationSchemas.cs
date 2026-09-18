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
/// Two objects that might be expected here are deliberately absent.
/// </para>
/// <para>
/// The license audit throttle is not shared. PostSharp 2026.0 keys it by the identity of the license, and this
/// version keys it by a hash of the audit report, so an entry of one would never match a lookup of the other; the
/// two would write into one key and neither would read what the other wrote.
/// </para>
/// <para>
/// The toast notifications are not shared. PostSharp 2026.0 has a handful of named settings for the questions it
/// asks, and this version has a snooze and a mute for each kind of notification. The two do not correspond, and a
/// mapping between them would be invented rather than observed.
/// </para>
/// </remarks>
[PublicAPI]
public static class PostSharpConfigurationSchemas
{
    /// <summary>
    /// Creates the schemas.
    /// </summary>
    /// <param name="dateTimeProvider">The clock, which the licensing schema needs to write the timestamp that tells
    /// PostSharp 2026.0 that the registered licenses have changed.</param>
    public static IRegistryConfigurationSchema[] Create( IDateTimeProvider dateTimeProvider )
        =>
        [
            new PostSharpLicensingConfigurationSchema( dateTimeProvider ),
            new PostSharpTelemetryConfigurationSchema(),
            new PostSharpLicenseServerConfigurationSchema()
        ];
}
