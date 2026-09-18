// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Licensing.Consumption;
using System.Globalization;

namespace SharpCrafters.Backstage.Licensing.Audit;

/// <summary>
/// Decides the identity under which an audit is throttled, which is what makes two audits the same audit.
/// </summary>
/// <remarks>
/// <para>
/// A license is audited at most once a day, and the identity decides what "the same audit" means. That is a product
/// decision and not a property of auditing: a product answers either "has this report been sent today?" or "has this
/// license been audited today?", and the two differ whenever anything else in the report changes, such as the
/// version of the product or the consent to usage reporting.
/// </para>
/// <para>
/// It matters beyond taste when a product shares the record with its earlier versions. Two versions that key the
/// record differently write into one store and neither reads what the other wrote, so each audits what the other has
/// just audited.
/// </para>
/// </remarks>
[PublicAPI]
public interface ILicenseAuditKeyProvider
{
    /// <summary>
    /// Gets the identity under which the audit of a license is throttled.
    /// </summary>
    /// <param name="license">The license being audited.</param>
    /// <param name="reportHashCode">
    /// A hash of the content of the report, which identifies the report rather than the license: it changes when the
    /// version of the product, the build date or the consent to usage reporting changes.
    /// </param>
    /// <returns>The identity, which is the key under which the moment of the last audit is recorded.</returns>
    string GetAuditKey( LicenseConsumptionProperties license, long reportHashCode );
}

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
    /// The hash is rendered in decimal, which is how it was written when this record was keyed by the number itself,
    /// so an existing record keeps being read.
    /// </remarks>
    public string GetAuditKey( LicenseConsumptionProperties license, long reportHashCode )
        => reportHashCode.ToString( CultureInfo.InvariantCulture );
}
