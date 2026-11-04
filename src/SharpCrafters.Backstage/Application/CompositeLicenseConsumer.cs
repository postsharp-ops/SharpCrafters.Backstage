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

namespace SharpCrafters.Backstage.Application;

/// <summary>
/// The <see cref="ILicenseConsumer"/> that <see cref="CompositeLicenseConsumptionService"/> creates. It holds the
/// consumer of each product, the primary product first.
/// </summary>
internal sealed class CompositeLicenseConsumer : ILicenseConsumer
{
    private readonly ImmutableArray<LicenseConsumer> _consumers;
    private readonly ILicenseProductCatalog _catalog;
    private readonly IEventDispatcher _eventDispatcher;

    public CompositeLicenseConsumer( ImmutableArray<LicenseConsumer> consumers, ILicenseProductCatalog catalog, IEventDispatcher eventDispatcher )
    {
        this._consumers = consumers;
        this._catalog = catalog;
        this._eventDispatcher = eventDispatcher;
        this.Licenses = consumers.SelectMany( c => c.Licenses ).ToImmutableArray();
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
    /// and one notification, that names the licenses of all products and the eligible products of the union catalog.
    /// </para>
    /// </remarks>
    public bool TryConsume( LicenseRequirement requirement, Action<LicensingMessage>? reportMessage = null, bool showsToastNotification = true )
    {
        var heldMessages = new List<LicensingMessage>();

        foreach ( var consumer in this._consumers )
        {
            var messages = new List<LicensingMessage>();

            if ( consumer.TryConsumeCore( requirement, messages.Add ) )
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
