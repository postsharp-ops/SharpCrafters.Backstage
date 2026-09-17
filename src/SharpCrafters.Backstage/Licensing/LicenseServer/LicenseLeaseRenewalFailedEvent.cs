// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Extensibility;
using System;

namespace SharpCrafters.Backstage.Licensing.LicenseServer;

/// <summary>
/// The event published when a license server could not be reached to renew a lease that is still valid. The build
/// goes on with the lease it holds; the user interface subscribes in order to warn the user, because that lease has
/// an end and nothing else will tell them it is coming.
/// </summary>
/// <remarks>
/// This is deliberately not reported to the build. A developer whose license server is briefly down keeps working,
/// and a warning on every compilation would be noise they cannot act upon at that moment. A notification reaches the
/// person rather than the build, once rather than per project, and while there is still time to do something about
/// it: the lease is renewed a day before it ends, so this is the day in which somebody has to notice.
/// </remarks>
/// <param name="LicenseServerUrl">The URL of the license server that could not be reached.</param>
/// <param name="LeaseEndTime">The instant at which the lease currently held stops licensing the product.</param>
/// <param name="ErrorMessage">The reason the server could not be reached.</param>
[PublicAPI]
public sealed record LicenseLeaseRenewalFailedEvent( string LicenseServerUrl, DateTime LeaseEndTime, string ErrorMessage ) : IDispatcherEvent;
