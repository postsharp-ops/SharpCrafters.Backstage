// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Configuration;
using System;
using System.Collections.Immutable;

namespace SharpCrafters.Backstage.Licensing.Audit;

[ConfigurationFile( "audit.json" )]
[PublicAPI]
public record LicenseAuditConfiguration : ConfigurationFile
{
    /// <summary>
    /// Gets the moment of the last audit of each audited thing, keyed by the identity that
    /// <see cref="ILicenseAuditKeyProvider"/> gives it.
    /// </summary>
    /// <remarks>
    /// The key is a string and not the number it used to be, because what identifies an audit is a product decision
    /// and not every product identifies it by a number. The default provider renders its number in decimal, which is
    /// how a number was written, so an existing record keeps being read.
    /// </remarks>
    public ImmutableDictionary<string, DateTime> LastAuditTimes { get; init; } =
        ImmutableDictionary<string, DateTime>.Empty;

    public DateTime? LastMatomoAuditTime { get; init; }

    [Obsolete]
    public bool IsFirstMatomoAudit { get; init; } = true;
}