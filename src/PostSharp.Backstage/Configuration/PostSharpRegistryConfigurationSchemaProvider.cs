// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Configuration.Registry;
using SharpCrafters.Backstage.Infrastructure;
using System.Collections.Generic;

namespace PostSharp.Backstage.Configuration;

/// <summary>
/// Names the configuration objects that PostSharp keeps in the Windows registry, which are the ones it shares with
/// PostSharp 2026.0: the registered licenses, the telemetry consents, the leases and the record of the audits.
/// </summary>
[PublicAPI]
public sealed class PostSharpRegistryConfigurationSchemaProvider : IRegistryConfigurationSchemaProvider
{
    private readonly IDateTimeProvider _dateTimeProvider;

    public PostSharpRegistryConfigurationSchemaProvider( IDateTimeProvider dateTimeProvider )
    {
        this._dateTimeProvider = dateTimeProvider;
    }

    /// <inheritdoc />
    public IEnumerable<IRegistryConfigurationSchema> GetSchemas() => PostSharpConfigurationSchemas.Create( this._dateTimeProvider );
}
