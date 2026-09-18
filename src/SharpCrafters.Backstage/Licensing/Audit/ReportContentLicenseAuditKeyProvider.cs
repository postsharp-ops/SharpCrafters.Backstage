// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Licensing.Consumption;

namespace SharpCrafters.Backstage.Licensing.Audit;

/// <summary>
/// Throttles an audit by the content of its report, so that a report is sent again whenever anything in it changes.
/// </summary>
/// <remarks>
/// This is the default, and it is what a product uses when it shares the record with nothing.
/// </remarks>
[PublicAPI]
public sealed class ReportContentLicenseAuditKeyProvider : ILicenseAuditKeyProvider
{
    /// <summary>
    /// Gets the single instance of the provider.
    /// </summary>
    public static ReportContentLicenseAuditKeyProvider Instance { get; } = new();

    private ReportContentLicenseAuditKeyProvider() { }

    /// <inheritdoc />
    /// <remarks>
    /// The hash is the identity itself, and it goes in the record that every released version reads as numbers, which
    /// is where this product has always written it.
    /// </remarks>
    public LicenseAuditKey GetAuditKey( LicenseConsumptionProperties license, long reportHashCode )
        => LicenseAuditKey.FromNumber( reportHashCode );
}
