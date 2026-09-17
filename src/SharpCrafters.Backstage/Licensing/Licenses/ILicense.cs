// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Licensing.Consumption;
using System.Threading;
using System.Threading.Tasks;

namespace SharpCrafters.Backstage.Licensing.Licenses
{
    /// <summary>
    /// A license providing licensed features.
    /// </summary>
    /// <remarks>
    /// The members are asynchronous because a licence leased from a license server is fetched over HTTP. A licence
    /// that is a key completes synchronously, which is why they return <see cref="ValueTask{TResult}"/>: the common
    /// case then allocates nothing.
    /// </remarks>
    internal interface ILicense
    {
        /// <summary>
        /// Gets the reason why the licence cannot be registered, or <see langword="null"/> when it can be.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token.</param>
        ValueTask<string?> GetRegistrationBlockerAsync( CancellationToken cancellationToken = default );

        /// <summary>
        /// Retrieves, or deserializes and validates, the licence data relevant to licence consumption. The data is
        /// either deserialized, from a licence key, or retrieved from a licence provider, such as a license server.
        /// </summary>
        /// <param name="options">The options of the consumption.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        ValueTask<LicenseConsumptionResult> GetConsumptionPropertiesAsync(
            LicenseConsumptionOptions options,
            CancellationToken cancellationToken = default );

        /// <summary>
        /// Retrieves the data relevant to licence registration, without validating every property.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token.</param>
        ValueTask<LicenseRegistrationPropertiesResult> GetRegistrationPropertiesAsync( CancellationToken cancellationToken = default );

        /// <summary>
        /// This method must be called once per day when the license is consumed.
        /// </summary>
        void ReportUse();
    }
}
