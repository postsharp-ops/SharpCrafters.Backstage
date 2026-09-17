// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using System;

namespace SharpCrafters.Backstage.Licensing.Registration;

/// <summary>
/// The times of a lease held from a license server, as they are presented to the user.
/// </summary>
/// <param name="StartTime">The instant at which the lease began, in UTC.</param>
/// <param name="EndTime">The instant after which the lease is void, in UTC.</param>
/// <param name="RenewTime">The instant from which the product renews the lease, in UTC.</param>
[PublicAPI]
public sealed record LicenseLeaseProperties( DateTime StartTime, DateTime EndTime, DateTime RenewTime );
