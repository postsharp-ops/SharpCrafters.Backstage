// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Configuration;
using System;
using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace SharpCrafters.Backstage.Licensing.Audit;

[ConfigurationFile( "audit.json" )]
[PublicAPI]
public record LicenseAuditConfiguration : ConfigurationFile
{
    /// <summary>
    /// Gets the moment of the last audit of each audited thing that a number identifies, which is every one of them
    /// for a product whose identities are hashes of the content of a report.
    /// </summary>
    /// <remarks>
    /// Keyed by a number because every version of the product reads this file, and the versions released before an
    /// identity could be anything else read the key as a number. Writing something else here would make them fail to
    /// read a file this version has written, on a machine where both are installed. An identity that is not one of
    /// these numbers goes to <see cref="LastAuditTimesByString"/>, which those versions ignore — including an identity
    /// that merely looks like a number, because it is not one of these.
    /// </remarks>
    [JsonPropertyName( "LastAuditTimes" )]
    public ImmutableDictionary<long, DateTime> LastAuditTimesByLong { get; init; } = ImmutableDictionary<long, DateTime>.Empty;

    /// <summary>
    /// Gets the moment of the last audit of each audited thing that <see cref="ILicenseAuditKeyProvider"/> identifies
    /// by something other than one of those numbers.
    /// </summary>
    /// <remarks>
    /// What identifies an audit is a product decision: PostSharp identifies a licence by its globally unique
    /// identifier, or by its number when the licence key carries none, and neither belongs among the hashes that
    /// <see cref="LastAuditTimesByLong"/> holds. The member is absent from the file when it is empty, so a product whose
    /// identities are all numbers writes the file it has always written.
    /// </remarks>
    [JsonPropertyName( "LastAuditTimesByKey" )]
    [JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
    public ImmutableDictionary<string, DateTime>? LastAuditTimesByString { get; init; }

    public DateTime? LastMatomoAuditTime { get; init; }

    [Obsolete]
    public bool IsFirstMatomoAudit { get; init; } = true;

    /// <summary>
    /// Gets the moment at which something was last audited.
    /// </summary>
    /// <param name="auditKey">The identity that <see cref="ILicenseAuditKeyProvider"/> gives it.</param>
    /// <param name="lastAuditTime">The moment of the last audit.</param>
    /// <returns><see langword="false"/> when it has never been audited.</returns>
    public bool TryGetLastAuditTime( LicenseAuditKey auditKey, out DateTime lastAuditTime )
    {
        if ( auditKey.Value is long number )
        {
            return this.LastAuditTimesByLong.TryGetValue( number, out lastAuditTime );
        }
        else if ( auditKey.Value is string text && this.LastAuditTimesByString is { } byKey )
        {
            return byKey.TryGetValue( text, out lastAuditTime );
        }
        else
        {
            lastAuditTime = default;

            return false;
        }
    }

    /// <summary>
    /// Records that something was audited.
    /// </summary>
    /// <param name="auditKey">The identity that <see cref="ILicenseAuditKeyProvider"/> gives it.</param>
    /// <param name="lastAuditTime">The moment of the audit.</param>
    public LicenseAuditConfiguration SetLastAuditTime( LicenseAuditKey auditKey, DateTime lastAuditTime )
    {
        if ( auditKey.Value is long number )
        {
            return this with { LastAuditTimesByLong = this.LastAuditTimesByLong.SetItem( number, lastAuditTime ) };
        }
        else if ( auditKey.Value is string text )
        {
            return this with
            {
                LastAuditTimesByString = (this.LastAuditTimesByString ?? ImmutableDictionary<string, DateTime>.Empty)
                    .SetItem( text, lastAuditTime )
            };
        }
        else
        {
            // A default instance identifies nothing, so there is no record to put it in. Recording it under some
            // substitute would throttle the audit of every license that also failed to be identified.
            throw new ArgumentException( "The audit key identifies nothing.", nameof(auditKey) );
        }
    }
}
