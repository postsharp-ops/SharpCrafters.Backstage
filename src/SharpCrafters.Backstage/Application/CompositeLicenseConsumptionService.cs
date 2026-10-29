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
    /// <para>
    /// A license key of the project, given by <see cref="LicenseConsumptionOptions.ProjectLicenseKey"/> or
    /// <see cref="LicenseConsumptionOptions.ExplicitLicenses"/>, is given only to the product that owns it (see
    /// <see cref="CompositeProductServices.GetOwner(string)"/>). Another product would report the key as a key of
    /// another product family, although the composite product can use it.
    /// </para>
    /// <para>
    /// A message that several products report in the same words is reported once.
    /// </para>
    /// </remarks>
    public async ValueTask<ILicenseConsumer> CreateConsumerAsync(
        LicenseConsumptionOptions? options = null,
        Action<LicensingMessage>? reportMessage = null,
        CancellationToken cancellationToken = default )
    {
        options ??= LicenseConsumptionOptions.Default;

        var reportedMessages = new HashSet<string>( StringComparer.Ordinal );
        var consumers = ImmutableArray.CreateBuilder<LicenseConsumer>( this._products.Products.Length );

        foreach ( var product in this._products.Products )
        {
            var productOptions = options with
            {
                ProjectLicenseKey = this._products.GetOwner( options.ProjectLicenseKey ) == product ? options.ProjectLicenseKey : null,
                ExplicitLicenses = options.ExplicitLicenses.IsDefault
                    ? options.ExplicitLicenses
                    : options.ExplicitLicenses.Where( l => this._products.GetOwner( l.LicenseString ) == product ).ToImmutableArray()
            };

            var consumer = await product.LicenseConsumptionService.CreateConsumerAsync( productOptions, ReportMessage, cancellationToken );

            // The service of a product is always the standard service, which creates this type.
            consumers.Add( (LicenseConsumer) consumer );
        }

        return new CompositeLicenseConsumer(
            consumers.MoveToImmutable(),
            this._products.ServiceProvider.GetRequiredBackstageService<ILicenseProductCatalog>(),
            this._products.ServiceProvider.GetRequiredBackstageService<IEventDispatcher>() );

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
}
