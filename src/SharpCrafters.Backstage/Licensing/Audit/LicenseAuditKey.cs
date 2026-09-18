// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using System;
using System.Globalization;

namespace SharpCrafters.Backstage.Licensing.Audit;

/// <summary>
/// The identity under which the audit of a license is recorded, and thereby the record it is recorded in.
/// </summary>
/// <remarks>
/// <para>
/// There are two records, because there are two kinds of identity and they are not interchangeable. A number is what
/// the versions released before an identity could be anything else read, and they read it as a number; anything else
/// has to go beside those, where a version that parses numbers does not look.
/// </para>
/// <para>
/// Which record an identity belongs in is known only to the product that produced it, so
/// <see cref="ILicenseAuditKeyProvider"/> states it here rather than leaving it to be worked out later from the shape
/// of the identity. Working it out is what this type replaces, and it was wrong: PostSharp identifies a license by
/// its number when the license key carries no globally unique identifier, so an identity such as <c>22</c> was filed
/// as though it were one of the numbers of the other record, which are hashes of the content of a report. The two
/// mean nothing to each other, and nothing said so.
/// </para>
/// <para>
/// The identity is held as a single value rather than as one field per kind, so that it is one or the other and
/// cannot be both. That makes this a union written by hand, and it is meant to become a real one: when the language
/// has unions, the two cases become the two members of one, the <see cref="object"/> and the boxing go, and the
/// callers that already switch on the case keep reading the same way.
/// </para>
/// </remarks>
[PublicAPI]
public readonly record struct LicenseAuditKey
{
    private LicenseAuditKey( object value )
    {
        this.Value = value;
    }

    /// <summary>
    /// Gets the identity: a <see cref="long"/> for the record that every released version reads, a
    /// <see cref="string"/> for the one beside it, and <see langword="null"/> for the identity of nothing, which is
    /// what a <see langword="default" /> instance holds.
    /// </summary>
    public object? Value { get; }

    /// <summary>
    /// Creates an identity that every released version can read, which is recorded in
    /// <see cref="LicenseAuditConfiguration.LastAuditTimesByLong"/>.
    /// </summary>
    /// <param name="number">The number, which is a hash of the content of the report for the products that use one.</param>
    public static LicenseAuditKey FromNumber( long number ) => new( number );

    /// <summary>
    /// Creates an identity that only the versions knowing the second record can read, which is recorded in
    /// <see cref="LicenseAuditConfiguration.LastAuditTimesByString"/>.
    /// </summary>
    /// <param name="text">
    /// The identity. It may look like a number — PostSharp identifies a license by its number when the license key
    /// carries no globally unique identifier — and it is still not one of the numbers of the other record.
    /// </param>
    public static LicenseAuditKey FromText( string text ) => new( text ?? throw new ArgumentNullException( nameof(text) ) );

    /// <inheritdoc />
    public override string ToString()
        => this.Value switch
        {
            long number => number.ToString( CultureInfo.InvariantCulture ),
            string text => text,
            _ => "(none)"
        };
}
