// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Licensing.Consumption;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SharpCrafters.Backstage.Application;

/// <summary>
/// The <see cref="ILicenseConsumptionService"/> of a composite product. Its consumer satisfies a requirement when the
/// licenses of any product satisfy it.
/// </summary>
internal sealed class CompositeLicenseConsumptionService : ILicenseConsumptionService
{
    private readonly CompositeProductServices _products;

    public CompositeLicenseConsumptionService( CompositeProductServices products )
    {
        this._products = products;

        foreach ( var product in products.Products )
        {
            product.LicenseConsumptionService.Changed += this.OnChanged;
        }
    }

    private void OnChanged() => this.Changed?.Invoke();

    /// <inheritdoc />
    /// <remarks>
    /// The consumer of each product is given the same options, so the license keys of the project are offered to every
    /// product. A message that several products report in the same words is reported once.
    /// </remarks>
    public async ValueTask<ILicenseConsumer> CreateConsumerAsync(
        LicenseConsumptionOptions? options = null,
        Action<LicensingMessage>? reportMessage = null,
        CancellationToken cancellationToken = default )
    {
        var reportedMessages = new HashSet<string>( StringComparer.Ordinal );
        var consumers = ImmutableArray.CreateBuilder<ILicenseConsumer>( this._products.Products.Length );

        foreach ( var product in this._products.Products )
        {
            consumers.Add( await product.LicenseConsumptionService.CreateConsumerAsync( options, ReportMessage, cancellationToken ) );
        }

        return new CompositeLicenseConsumer( consumers.MoveToImmutable() );

        void ReportMessage( LicensingMessage message )
        {
            if ( reportMessage == null )
            {
                return;
            }

            lock ( reportedMessages )
            {
                if ( !reportedMessages.Add( message.Text ) )
                {
                    return;
                }
            }

            reportMessage( message );
        }
    }

    [Obsolete( "Use CreateConsumerAsync." )]
    public ILicenseConsumer CreateConsumer( LicenseConsumptionOptions? options = null, Action<LicensingMessage>? reportMessage = null )

        // See LicenseConsumptionService.CreateConsumer for why the work is started on the thread pool.
        => Task.Run( () => this.CreateConsumerAsync( options, reportMessage, CancellationToken.None ).AsTask() )
            .GetAwaiter()
            .GetResult();

    public event Action? Changed;

    /// <summary>
    /// A consumer that holds the consumer of each product, the primary product first.
    /// </summary>
    private sealed class CompositeLicenseConsumer : ILicenseConsumer
    {
        private readonly ImmutableArray<ILicenseConsumer> _consumers;

        public CompositeLicenseConsumer( ImmutableArray<ILicenseConsumer> consumers )
        {
            this._consumers = consumers;
            this.Licenses = consumers.SelectMany( c => c.Licenses ).ToImmutableArray();
        }

        public ImmutableArray<LicenseConsumptionProperties> Licenses { get; }

        /// <inheritdoc />
        /// <remarks>
        /// <para>
        /// The consumers are asked in order, without a notification and with their messages held back. The first one
        /// that satisfies the requirement wins: it marks its license for audit, and its messages are reported.
        /// </para>
        /// <para>
        /// When no consumer satisfies the requirement, the consumer of the primary product is asked again, and this time
        /// it reports its messages and publishes its notification, so the user is told once.
        /// </para>
        /// </remarks>
        public bool TryConsume( LicenseRequirement requirement, Action<LicensingMessage>? reportMessage = null, bool showsToastNotification = true )
        {
            foreach ( var consumer in this._consumers )
            {
                var messages = new List<LicensingMessage>();

                if ( consumer.TryConsume( requirement, messages.Add, false ) )
                {
                    if ( reportMessage != null )
                    {
                        messages.ForEach( reportMessage );
                    }

                    return true;
                }
            }

            return this._consumers[0].TryConsume( requirement, reportMessage, showsToastNotification );
        }
    }
}
