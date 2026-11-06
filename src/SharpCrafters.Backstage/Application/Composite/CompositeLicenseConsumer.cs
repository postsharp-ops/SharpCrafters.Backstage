// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Licensing;
using SharpCrafters.Backstage.Licensing.Consumption;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace SharpCrafters.Backstage.Application.Composite;

/// <summary>
/// The <see cref="ILicenseConsumer"/> that <see cref="CompositeLicenseConsumptionService"/> creates. It holds the
/// consumer of each product, the primary product first.
/// </summary>
/// <remarks>
/// A license key that several products hold, because the composite registers a key in every store that accepts it, is
/// considered only by the consumer of the product to which it is native. That product consumes it, whatever the
/// requirement, so the key is audited once, by that product. The key is also listed once in <see cref="Licenses"/>.
/// </remarks>
internal sealed class CompositeLicenseConsumer : ILicenseConsumer
{
    private readonly ImmutableArray<LicenseConsumer> _consumers;
    private readonly ImmutableArray<Func<LicenseConsumptionProperties, bool>> _isIgnored;
    private readonly ILicenseProductCatalog _catalog;
    private readonly IEventDispatcher _eventDispatcher;

    /// <param name="consumers">The consumer of each member, in the order of <see cref="CompositeProductMembers.Members"/>.</param>
    /// <param name="members">The members.</param>
    /// <param name="catalog">The catalog that names the products that would satisfy a requirement.</param>
    /// <param name="eventDispatcher">The dispatcher of the notification of an unsatisfied requirement.</param>
    public CompositeLicenseConsumer(
        ImmutableArray<LicenseConsumer> consumers,
        CompositeProductMembers members,
        ILicenseProductCatalog catalog,
        IEventDispatcher eventDispatcher )
    {
        this._consumers = consumers;
        this._catalog = catalog;
        this._eventDispatcher = eventDispatcher;

        var licenseStrings = consumers
            .Select( c => new HashSet<string>( c.Licenses.Select( l => l.LicenseString ).OfType<string>(), StringComparer.Ordinal ) )
            .ToImmutableArray();

        // A license is ignored by a consumer when the member to which it is native is another member that holds it too.
        this._isIgnored = consumers.Select<LicenseConsumer, Func<LicenseConsumptionProperties, bool>>(
                ( _, index ) => license =>
                {
                    if ( license.LicenseString == null )
                    {
                        return false;
                    }

                    var nativeIndex = members.Members.IndexOf( members.GetMemberForProduct( license.LicenseProduct ) );

                    return nativeIndex != index && licenseStrings[nativeIndex].Contains( license.LicenseString );
                } )
            .ToImmutableArray();

        this.Licenses = consumers.SelectMany( ( c, index ) => c.Licenses.Where( l => !this._isIgnored[index]( l ) ) ).ToImmutableArray();
    }

    public ImmutableArray<LicenseConsumptionProperties> Licenses { get; }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// The consumers are asked in order, and their messages are held back. The first one that satisfies the
    /// requirement wins: it marks its license for audit, and its messages are reported.
    /// </para>
    /// <para>
    /// When no consumer satisfies the requirement, the messages of all consumers are reported, and then one message,
    /// and one notification, that names the licenses of all products and the products that would satisfy the
    /// requirement.
    /// </para>
    /// </remarks>
    public bool TryConsume( LicenseRequirement requirement, Action<LicensingMessage>? reportMessage = null, bool showsToastNotification = true )
    {
        var heldMessages = new List<LicensingMessage>();

        for ( var index = 0; index < this._consumers.Length; index++ )
        {
            var messages = new List<LicensingMessage>();

            if ( this._consumers[index].TryConsumeCore( requirement, messages.Add, this._isIgnored[index] ) )
            {
                if ( reportMessage != null )
                {
                    messages.ForEach( reportMessage );
                }

                return true;
            }

            heldMessages.AddRange( messages );
        }

        if ( reportMessage != null )
        {
            heldMessages.GroupBy( m => m.Text, StringComparer.Ordinal ).Select( g => g.First() ).ToList().ForEach( reportMessage );
        }

        LicenseConsumer.ReportRequirementNotSatisfied(
            requirement,
            this._catalog,
            this.Licenses,
            this._eventDispatcher,
            reportMessage,
            showsToastNotification );

        return false;
    }
}
