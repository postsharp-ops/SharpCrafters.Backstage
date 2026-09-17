// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

namespace SharpCrafters.Backstage.Licensing.LicenseServer;

/// <summary>
/// The result of asking a license server for a lease: either the lease, or the reason there is none.
/// </summary>
/// <param name="Lease">The lease, or <see langword="null"/> when there is none.</param>
/// <param name="ErrorMessage">The reason there is no lease, or <see langword="null"/> when there is one.</param>
internal readonly record struct LicenseLeaseResult( LicenseLease? Lease, string? ErrorMessage )
{
    public bool IsSuccess => this.Lease != null;

    public static LicenseLeaseResult Success( LicenseLease lease ) => new( lease, null );

    public static LicenseLeaseResult Failure( string errorMessage ) => new( null, errorMessage );
}
