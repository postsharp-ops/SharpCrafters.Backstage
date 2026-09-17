// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Extensibility;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SharpCrafters.Backstage.Licensing.Consumption
{
    /// <summary>
    /// Exposes a service to verify the current license and consume features from it.
    /// </summary>
    [PublicAPI]
    public interface ILicenseConsumptionService : IBackstageService
    {
        /// <summary>
        /// Creates an <see cref="ILicenseConsumer"/>, resolving every licence of every source.
        /// </summary>
        /// <param name="options">The options of the consumption.</param>
        /// <param name="reportMessage">A delegate that receives the message of each licence that is present but unusable.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <remarks>
        /// <para>
        /// This is the only asynchronous step of licence consumption: a licence may be leased from a license server,
        /// which is fetched over HTTP. Once the consumer exists, every licence is in hand, so
        /// <see cref="ILicenseConsumer.TryConsume"/> is synchronous and contacts nothing.
        /// </para>
        /// <para>
        /// <b>This is therefore where a seat is taken from the pool of a license server</b>, and it is taken whether or
        /// not a requirement ends up using the leased licence, exactly as a license key is read whether or not it is
        /// used. A stored lease that is still valid and not due for renewal costs neither a request nor a new seat, so
        /// one machine contacts the server about once every two days rather than once per build. See
        /// <c>docs/license-server.md</c>.
        /// </para>
        /// </remarks>
        ValueTask<ILicenseConsumer> CreateConsumerAsync(
            LicenseConsumptionOptions? options = null,
            Action<LicensingMessage>? reportMessage = null,
            CancellationToken cancellationToken = default );

        /// <summary>
        /// Creates an <see cref="ILicenseConsumer"/>, blocking until every licence is resolved.
        /// </summary>
        [Obsolete(
            "Use CreateConsumerAsync. This overload blocks the calling thread while a license server is contacted, "
            + "which stalls a user interface thread." )]
        ILicenseConsumer CreateConsumer( LicenseConsumptionOptions? options = null, Action<LicensingMessage>? reportMessage = null );

        /// <summary>
        /// Event raised when user-profile license keys have changed.
        /// </summary>
        event Action? Changed;
    }
}
