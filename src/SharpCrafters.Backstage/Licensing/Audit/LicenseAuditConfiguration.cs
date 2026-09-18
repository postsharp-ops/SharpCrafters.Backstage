// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Configuration;
using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json.Serialization;

namespace SharpCrafters.Backstage.Licensing.Audit;

[ConfigurationFile( "audit.json" )]
[PublicAPI]
public record LicenseAuditConfiguration : ConfigurationFile
{
    /// <summary>
    /// Gets the moment of the last audit of each licence, keyed by the number that identifies it.
    /// </summary>
    /// <remarks>
    /// Keyed by a number, and not by the string that <see cref="ILicenseAuditKeyProvider"/> returns, because every
    /// version of the product reads this file and the versions released before that provider existed read the key as
    /// a number. Writing something else here would make them fail to read a file this version has written, on a
    /// machine where both are installed. An identity that is not a number goes to
    /// <see cref="LastAuditTimesByKey"/>, which those versions ignore.
    /// </remarks>
    public ImmutableDictionary<long, DateTime> LastAuditTimes { get; init; } = ImmutableDictionary<long, DateTime>.Empty;

    /// <summary>
    /// Gets the moment of the last audit of each audited thing that no number identifies, keyed by the identity that
    /// <see cref="ILicenseAuditKeyProvider"/> gives it.
    /// </summary>
    /// <remarks>
    /// What identifies an audit is a product decision, and not every product identifies it by a number: PostSharp
    /// identifies a licence by a globally unique identifier when it has one. The member is absent from the file when
    /// it is empty, so a product whose identities are all numbers writes the file it has always written.
    /// </remarks>
    [JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
    public ImmutableDictionary<string, DateTime>? LastAuditTimesByKey { get; init; }

    public DateTime? LastMatomoAuditTime { get; init; }

    [Obsolete]
    public bool IsFirstMatomoAudit { get; init; } = true;

    /// <summary>
    /// Gets the moment at which something was last audited.
    /// </summary>
    /// <param name="auditKey">The identity that <see cref="ILicenseAuditKeyProvider"/> gives it.</param>
    /// <param name="lastAuditTime">The moment of the last audit.</param>
    /// <returns><see langword="false"/> when it has never been audited.</returns>
    public bool TryGetLastAuditTime( string auditKey, out DateTime lastAuditTime )
    {
        if ( TryParseNumericKey( auditKey, out var numericKey ) )
        {
            return this.LastAuditTimes.TryGetValue( numericKey, out lastAuditTime );
        }

        if ( this.LastAuditTimesByKey is { } byKey )
        {
            return byKey.TryGetValue( auditKey, out lastAuditTime );
        }

        lastAuditTime = default;

        return false;
    }

    /// <summary>
    /// Records that something was audited.
    /// </summary>
    /// <param name="auditKey">The identity that <see cref="ILicenseAuditKeyProvider"/> gives it.</param>
    /// <param name="lastAuditTime">The moment of the audit.</param>
    public LicenseAuditConfiguration SetLastAuditTime( string auditKey, DateTime lastAuditTime )
        => TryParseNumericKey( auditKey, out var numericKey )
            ? this with { LastAuditTimes = this.LastAuditTimes.SetItem( numericKey, lastAuditTime ) }
            : this with
            {
                LastAuditTimesByKey = (this.LastAuditTimesByKey ?? ImmutableDictionary<string, DateTime>.Empty)
                    .SetItem( auditKey, lastAuditTime )
            };

    /// <summary>
    /// Determines whether an identity is one that the versions reading this file as a number understand.
    /// </summary>
    /// <remarks>
    /// The rendered form has to match the identity exactly, and not merely parse: an identity with a leading zero or
    /// a sign parses to a number that is written back differently, and the entry would then be found under a name
    /// other than the one it was stored under.
    /// </remarks>
    private static bool TryParseNumericKey( string auditKey, out long numericKey )
        => long.TryParse( auditKey, NumberStyles.None, CultureInfo.InvariantCulture, out numericKey )
           && string.Equals( numericKey.ToString( CultureInfo.InvariantCulture ), auditKey, StringComparison.Ordinal );
}
