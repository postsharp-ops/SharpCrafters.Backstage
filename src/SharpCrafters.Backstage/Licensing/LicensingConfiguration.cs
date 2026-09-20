// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Configuration;
using SharpCrafters.Backstage.Licensing.Consumption;
using SharpCrafters.Backstage.Licensing.LicenseServer;
using SharpCrafters.Backstage.Licensing.Licenses;
using SharpCrafters.Backstage.Licensing.Registration;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json.Serialization;

namespace SharpCrafters.Backstage.Licensing;

/// <summary>
/// The license keys that the user has registered, and the state that goes with them.
/// </summary>
/// <remarks>
/// Public because a product whose earlier versions keep these settings somewhere other than a file supplies the
/// schema that maps them, and a schema is part of the product package rather than of the neutral services.
/// </remarks>
[ConfigurationFile( "licensing.json" )]
[PublicAPI]
public sealed record LicensingConfiguration : ConfigurationFile
{
    /// <summary>
    /// Gets the date of the last evaluation period.
    /// </summary>
    [JsonPropertyName( "lastEvaluationStartDate" )]
    public DateTime? LastEvaluationStartDate { get; init; }

    /// <summary>
    /// Gets the only license key compatible with pre-2025.1, or <c>null</c> if there is none.
    /// </summary>
    [JsonPropertyName( "license" )]
    public string? LegacyLicense { get; init; }

    private readonly ImmutableArray<string?> _licenses = ImmutableArray<string?>.Empty;

    /// <summary>
    /// Gets the list of license keys for Metalama 2025.1 or later.
    /// </summary>
    /// <remarks>
    /// The value is normalized to <see cref="ImmutableArray{T}.Empty"/> because a default (uninitialized)
    /// <see cref="ImmutableArray{T}"/> wraps a null array and throws when it is enumerated or serialized. A property
    /// initializer alone is not enough: the System.Text.Json source generator treats every <c>init</c> property as a
    /// constructor parameter and assigns it unconditionally, so a <c>licensing.json</c> without a <c>licenses</c> entry
    /// (a fresh installation, or a file written by an earlier version) overwrites the initializer with the default value.
    /// </remarks>
    [JsonPropertyName( "licenses" )]
    public ImmutableArray<string?> Licenses
    {
        get => this._licenses;
        init => this._licenses = value.IsDefault ? ImmutableArray<string?>.Empty : value;
    }

    /// <summary>
    /// Gets the license keys that no released version of Metalama can consume, grouped by the minimal version of
    /// Metalama that can consume them. The key of the group is that version, formatted by <see cref="Version.ToString()"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every installed version of Metalama reads the same <c>licensing.json</c>, so a license key that only a later
    /// version understands otherwise reaches the earlier versions, which report a message that the user cannot act
    /// upon, or throw. A version reads <see cref="LegacyLicense"/>, <see cref="Licenses"/> and the groups whose
    /// version is not greater than its own, and skips the other groups before their license keys are deserialized.
    /// </para>
    /// <para>
    /// The value is <c>null</c> when there is no group, and the member is then absent from the file, so registering
    /// a license key that every released version can consume writes the same file as before this member existed.
    /// </para>
    /// </remarks>
    [JsonPropertyName( "licensesByMinimalVersion" )]
    [JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
    public ImmutableDictionary<string, ImmutableArray<string?>>? LicensesByMinimalVersion { get; init; }

    /// <summary>
    /// Gets a value indicating whether the warning about a license server reached over an insecure <c>http://</c> URL
    /// is silenced.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Contacting a license server transmits the name of the user and the name of the machine, so an <c>http://</c>
    /// server discloses who works where to anyone on the path. That is worth a warning, but not an error: an
    /// on-premises server on a network the administrator considers safe is a legitimate deployment, and refusing it
    /// would fail a build over something the user cannot change from their side.
    /// </para>
    /// <para>
    /// This is the only way to silence the warning. PostSharp also read an MSBuild property and an environment
    /// variable, with three values; a single setting in the configuration of the user is enough, because the decision
    /// belongs to whoever registered the server and does not change from one build to the next.
    /// </para>
    /// </remarks>
    [JsonPropertyName( "allowInsecureLicenseServer" )]
    [JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
    public bool AllowInsecureLicenseServer { get; init; }

    public CommunityLicenseReason CommunityLicenseReason { get; init; }

    /// <summary>
    /// Registers a license key, removing the keys of the products that do not co-exist with it, and storing the key
    /// where the versions that support its product read it.
    /// </summary>
    /// <param name="license">The properties of the license key.</param>
    /// <param name="catalog">The catalog that gives the registration rules of the products.</param>
    public LicensingConfiguration SetLicense( LicenseRegistrationProperties license, ILicenseProductCatalog catalog )
    {
        // First we should remove previous licenses except if they must co-exist for backward-compatibility reasons.
        var coexistingProducts = catalog.GetProductsCoexistingWith( license.Product );
        var clone = coexistingProducts.IsEmpty ? this.RemoveAllLicenses() : this.RemoveAllLicensesExcept( coexistingProducts );

        var licenseString = license.LicenseString ?? throw new ArgumentNullException( nameof(license) );

        // Now we can add the new license, in the oldest group that can consume it.
        if ( license.MinVersion != null )
        {
            // Added to the group rather than put in its place, and the other groups are left alone, for the same
            // reason as in the list below: in a family whose products co-exist, every key reaches a group, and
            // replacing the groups would drop the keys that the registration is supposed to keep. The keys that do
            // not co-exist with this one have already been removed above.
            var groups = clone.LicensesByMinimalVersion ?? ImmutableDictionary<string, ImmutableArray<string?>>.Empty;
            var groupName = license.MinVersion.ToString();
            var group = groups.TryGetValue( groupName, out var existingGroup ) ? existingGroup : ImmutableArray<string?>.Empty;

            return clone with { LicensesByMinimalVersion = groups.SetItem( groupName, group.Add( licenseString ) ) };
        }
        else if ( catalog.IsStoredInLicenseList( license.Product ) )
        {
            // Added to the keys that survived, not put in their place: in a family whose products co-exist, the user
            // registers them one after another, and each registration has to keep the ones registered before it. The
            // keys that do not co-exist with this one have already been removed above.
            return clone with { Licenses = clone.Licenses.Add( licenseString ) };
        }
        else
        {
            return clone with { LegacyLicense = licenseString };
        }
    }

    public LicensingConfiguration RemoveAllLicenses()
        => this with { LegacyLicense = null, Licenses = ImmutableArray<string?>.Empty, LicensesByMinimalVersion = null };

    private LicensingConfiguration RemoveAllLicensesExcept( ImmutableArray<LicenseProduct> products )
    {
        // The groups are filtered by the same rule as the two other slots, and not cleared. A family whose products
        // co-exist and whose keys all reach a group -- which is PostSharp, whose every current key is kept from the
        // versions that cannot read it -- would otherwise lose every key it is supposed to keep each time another
        // one is registered.
        var clone = this;

        if ( clone.LicensesByMinimalVersion is { } groups )
        {
            var survivingGroups = ImmutableDictionary.CreateBuilder<string, ImmutableArray<string?>>();

            foreach ( var group in groups )
            {
                var survivingGroup = group.Value.RemoveAll( license => !CoexistsWithRegisteredLicense( license, products ) );

                // A group that nothing survives in is dropped rather than left empty, so that the configuration does
                // not accumulate the name of every version a user has ever registered a key for.
                if ( !survivingGroup.IsEmpty )
                {
                    survivingGroups.Add( group.Key, survivingGroup );
                }
            }

            clone = clone with { LicensesByMinimalVersion = survivingGroups.Count == 0 ? null : survivingGroups.ToImmutable() };
        }

        if ( clone.LegacyLicense != null && !CoexistsWithRegisteredLicense( clone.LegacyLicense, products ) )
        {
            clone = clone with { LegacyLicense = null };
        }

        // The list is filtered by the same rule as the single slot. Leaving it out would keep a key of a product that
        // the newly registered one replaces, and in a family that stores its keys in the list that is every key.
        var survivingLicenses = clone.Licenses.RemoveAll( license => !CoexistsWithRegisteredLicense( license, products ) );

        if ( survivingLicenses.Length != clone.Licenses.Length )
        {
            clone = clone with { Licenses = survivingLicenses };
        }

        return clone;
    }

    /// <summary>
    /// Determines whether an already registered license string survives the registration of a new license, given the
    /// products that co-exist with the product being registered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A license server URL is removed whatever the products that co-exist: it is not a license key of any product,
    /// so the co-existence rules of the catalog cannot apply to it. It is spelled out rather than left to fall into
    /// the branch for a string that does not parse, so that the reason is visible.
    /// </para>
    /// <para>
    /// The product of the stored key is normalized before it is compared, because the set of products comes from the
    /// catalog, which is asked about the normalized product of the license being registered. The two differ for the
    /// editions that a family expresses through the license type: the free edition of PostSharp is a PostSharp
    /// Ultimate key carrying the Essentials type, so comparing the product as written would read it as Ultimate,
    /// find that Ultimate co-exists with the edition being registered, and keep a key that the new one replaces.
    /// </para>
    /// </remarks>
    private static bool CoexistsWithRegisteredLicense( string? licenseString, ImmutableArray<LicenseProduct> products )
        => licenseString != null
           && !LicenseServerUrl.IsLicenseServerUrl( licenseString )
           && GetLicenseKeyData( licenseString ) is { } licenseKeyData
           && products.Contains( licenseKeyData.NormalizeProduct() );

    private static LicenseKeyData? GetLicenseKeyData( string? licenseKey, Action<LicensingMessage>? reportMessage = null )
    {
        if ( string.IsNullOrWhiteSpace( licenseKey ) )
        {
            return null;
        }

        // ReSharper disable once RedundantSuppressNullableWarningExpression
        if ( !LicenseKeyData.TryDeserialize( licenseKey!, out var licenseKeyData, out var errorMessage ) )
        {
            reportMessage?.Invoke( new LicensingMessage( errorMessage ) { Kind = LicensingMessageKind.InvalidLicenseKey } );

            return null;
        }

        return licenseKeyData;
    }

    /// <summary>
    /// Enumerates the groups of <see cref="LicensesByMinimalVersion"/> whose name parses as a version and that carry
    /// at least one non-blank license key, ordered by that version. A group whose name does not parse as a version is
    /// skipped, as a later version may name a group in a way that the current version does not understand. A group
    /// whose license keys are all blank is skipped as well, so that it behaves as an empty group and is not reported
    /// as requiring a later version of Metalama.
    /// </summary>
    private IEnumerable<(Version MinimalVersion, ImmutableArray<string?> Licenses)> GetLicenseGroups()
    {
        if ( this.LicensesByMinimalVersion == null )
        {
            return [];
        }

        return this.LicensesByMinimalVersion
            .Where( group => !group.Value.IsDefaultOrEmpty && group.Value.Any( licenseKey => !string.IsNullOrWhiteSpace( licenseKey ) ) )
            .Select( group => (IsVersion: System.Version.TryParse( group.Key, out var version ), MinimalVersion: version, group.Value) )
            .Where( group => group.IsVersion )
            .Select( group => (MinimalVersion: group.MinimalVersion!, Licenses: group.Value) )
            .OrderBy( group => group.MinimalVersion );
    }

    /// <summary>
    /// Gets the minimal versions of the groups that <paramref name="currentVersion"/> does not support, ordered by
    /// version. The license keys of those groups are not deserialized, so the version of the group is the only
    /// information that the running version has about them.
    /// </summary>
    /// <param name="currentVersion">The version of the running product.</param>
    /// <returns>The minimal versions of the unsupported groups.</returns>
    public IEnumerable<Version> GetUnsupportedMinimalVersions( Version currentVersion )
        => this.GetLicenseGroups().Where( group => group.MinimalVersion > currentVersion ).Select( group => group.MinimalVersion );

    /// <summary>
    /// Gets all parsable license keys that the running version supports, regardless of whether they are valid or not.
    /// The license keys of a group whose version is greater than <paramref name="currentVersion"/> are not returned
    /// and are not deserialized, so they report no message.
    /// </summary>
    /// <param name="currentVersion">The version of the running product.</param>
    /// <param name="reportMessage">A delegate that receives the message reported by a license key that does not parse.</param>
    /// <returns>The license key data of the license keys that the running version supports.</returns>
    public IEnumerable<LicenseKeyData> GetRegisteredLicenses( Version currentVersion, Action<LicensingMessage>? reportMessage = null )
    {
        foreach ( var license in this.EnumerateLicenseStrings( currentVersion ) )
        {
            // A license server URL is not a license key and is skipped silently, because reporting it as unparsable
            // would tell the user that what they registered is broken when it is not.
            if ( LicenseServerUrl.IsLicenseServerUrl( license ) )
            {
                continue;
            }

            var licenseKeyData = GetLicenseKeyData( license, reportMessage );

            if ( licenseKeyData != null )
            {
                yield return licenseKeyData;
            }
        }
    }

    /// <summary>
    /// Gets the license strings that the running version supports, which are the license keys that parse and the
    /// license server URLs, with the URLs last.
    /// </summary>
    /// <param name="currentVersion">The version of the running product.</param>
    /// <param name="reportMessage">A delegate that receives the message reported by a license key that does not parse.</param>
    /// <returns>The license strings that the running version supports.</returns>
    /// <remarks>
    /// <para>
    /// A URL is yielded verbatim and is never deserialized as a license key. This is what lets a license server reach
    /// <see cref="LicenseFactory"/>, which is the only place that decides what a license string is.
    /// </para>
    /// <para>
    /// The URLs come last so that a registered license key is always considered before a lease is acquired, which is
    /// the order PostSharp resolved them in: a key costs nothing, whereas a lease may cost a request and a seat.
    /// </para>
    /// </remarks>
    public IEnumerable<string> GetRegisteredLicenseStrings( Version currentVersion, Action<LicensingMessage>? reportMessage = null )
    {
        var urls = new List<string>();

        foreach ( var license in this.EnumerateLicenseStrings( currentVersion ) )
        {
            if ( LicenseServerUrl.IsLicenseServerUrl( license ) )
            {
                urls.Add( license! );
            }
            else if ( GetLicenseKeyData( license, reportMessage ) != null )
            {
                yield return license!;
            }
        }

        foreach ( var url in urls )
        {
            yield return url;
        }
    }

    /// <summary>
    /// Enumerates the license strings of every bucket that the running version reads, in the order in which the
    /// buckets were introduced, without parsing any of them.
    /// </summary>
    private IEnumerable<string?> EnumerateLicenseStrings( Version currentVersion )
        => new[] { this.LegacyLicense }
            .Concat( this.Licenses )
            .Concat( this.GetLicenseGroups().Where( group => group.MinimalVersion <= currentVersion ).SelectMany( group => group.Licenses ) );
}
