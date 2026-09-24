// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Utilities;
using System;
using System.Globalization;
using System.Reflection;

namespace SharpCrafters.Backstage.Application;

/// <summary>
/// Describes a component of a product from the metadata of one of its assemblies.
/// </summary>
/// <remarks>
/// <para>
/// The assembly that describes the component must have the following three attributes. The build does not fail
/// when one of them is missing, so a product that adopts these services must verify them.
/// </para>
/// <list type="number">
/// <item>
/// <description>
/// <c>[assembly: AssemblyMetadata( "PackageVersion", … )]</c> gives the version of the package that contains the
/// assembly. The telemetry and exception reports include it, and <see cref="IsPrerelease"/> is computed from it.
/// </description>
/// </item>
/// <item>
/// <description>
/// <c>[assembly: AssemblyMetadata( "PackageBuildDate", … )]</c> gives the build date. The value must be in a format
/// that <see cref="DateTime.Parse(string, IFormatProvider)"/> accepts with the invariant culture. Licensing compares
/// the build date with the end date of the subscription of a license key. When the build date is missing, the
/// consumption of a license throws an <see cref="InvalidOperationException"/>.
/// </description>
/// </item>
/// <item>
/// <description>
/// <c>[assembly: AssemblyCompany( … )]</c> must be equal to <see cref="ProductProfile.Company"/>. Licensing selects
/// the components of the vendor by this value. When the value is different, licensing ignores the component. It then
/// compares the subscription end date with the build date of another component.
/// </description>
/// </item>
/// </list>
/// <para>
/// Two environment variables override the prerelease flag and the build date for testing. Their names start with the
/// prefix of the product. For example, <c>METALAMA_BUILD_DATE</c> and <c>POSTSHARP_BUILD_DATE</c> are two different
/// variables.
/// </para>
/// </remarks>
public abstract class ComponentInfoBase : IComponentInfo
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ComponentInfoBase"/> class.
    /// </summary>
    /// <param name="metadataAssembly">The assembly whose metadata describes the component. It must carry the three attributes listed on this class.</param>
    /// <param name="productProfile">The profile of the product family, which names the environment variables that override the prerelease flag and the build date at build time.</param>
    protected ComponentInfoBase( Assembly metadataAssembly, ProductProfile productProfile )
    {
        var reader = AssemblyMetadataReader.GetInstance( metadataAssembly );
        this.PackageVersion = reader.PackageVersion;
        this.AssemblyVersion = reader.AssemblyVersion;

        // TODO: Check that these overrides cannot be abused to bypass licensing, for instance by presenting a release
        // build as a prerelease build to obtain the preview license, or by moving the build date. See #2018.

        // IsPrerelease flag can be overridden for testing purposes.
        var isPrereleaseEnvironmentVariableValue = Environment.GetEnvironmentVariable( productProfile.GetEnvironmentVariableName( "IS_PRERELEASE" ) );
        bool? isPrereleaseOverriddenValue = isPrereleaseEnvironmentVariableValue == null ? null : bool.Parse( isPrereleaseEnvironmentVariableValue );
        this.IsPrerelease = isPrereleaseOverriddenValue ?? (this.PackageVersion != null && VersionHelper.IsPrereleaseVersion( this.PackageVersion ));

        // BuildDate value can be overridden for testing purposes.
        var buildDateEnvironmentVariableValue = Environment.GetEnvironmentVariable( productProfile.GetEnvironmentVariableName( "BUILD_DATE" ) );

        DateTime? buildDateOverriddenValue = buildDateEnvironmentVariableValue == null
            ? null
            : DateTime.Parse( buildDateEnvironmentVariableValue, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal );

        this.BuildDate = buildDateOverriddenValue ?? reader.BuildDate;
        this.Company = reader.Company;
    }

    /// <inheritdoc />
    public string? Company { get; }

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public string? PackageVersion { get; }

    public Version? AssemblyVersion { get; }

    /// <inheritdoc />
    public bool? IsPrerelease { get; }

    /// <inheritdoc />
    public DateTime? BuildDate { get; }
}