// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Licensing.Audit;
using SharpCrafters.Backstage.Licensing.Consumption;
using SharpCrafters.Backstage.Licensing.Licenses;

namespace PostSharp.Backstage;

/// <summary>
/// Throttles an audit by the identity of the license, which is how PostSharp 2026.0 throttles it.
/// </summary>
/// <remarks>
/// <para>
/// PostSharp asks "has this license been audited today?" rather than "has this report been sent today?". The
/// difference is not academic while the two versions share the record: keyed by the content of the report, an entry
/// written by one version would never match a lookup by the other, because the report carries the version of the
/// product that wrote it. Each version would then audit every license the other had just audited.
/// </para>
/// <para>
/// The identity is the one PostSharp 2026.0 uses, which is the GUID of the license when it has one and its decimal
/// identifier otherwise.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class PostSharpLicenseAuditKeyProvider : ILicenseAuditKeyProvider
{
    /// <summary>
    /// Gets the single instance of the provider.
    /// </summary>
    public static PostSharpLicenseAuditKeyProvider Instance { get; } = new();

    private PostSharpLicenseAuditKeyProvider() { }

    /// <inheritdoc />
    /// <remarks>
    /// The identity is read from the license key itself, which is the only place it is recorded: the properties that
    /// reach a consumer do not carry it. A license that is not a key — the one an unattended build is given, or one
    /// a test invents — has no identity of its own, and is throttled by the content of its report instead, which is
    /// the behaviour of every other product.
    /// </remarks>
    public string GetAuditKey( LicenseConsumptionProperties license, long reportHashCode )
    {
        if ( license.LicenseString != null
             && LicenseKeyData.TryDeserialize( license.LicenseString, out var licenseKeyData, out _ ) )
        {
            return licenseKeyData.LicenseUniqueId;
        }

        return ReportContentLicenseAuditKeyProvider.Instance.GetAuditKey( license, reportHashCode );
    }
}
