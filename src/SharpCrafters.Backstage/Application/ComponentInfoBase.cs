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
/// <strong>What the assembly of a product must carry.</strong> A product built on these services has to produce
/// three things at build time, and none of them fails loudly when it is missing, so a product that adopts these
/// services checks all three before anything else:
/// </para>
/// <list type="number">
/// <item>
/// <description>
/// <c>[assembly: AssemblyMetadata( "PackageVersion", … )]</c>, the version of the package the assembly ships in.
/// A version-limited licence key is compared against it, and a usage report is grouped by it.
/// </description>
/// </item>
/// <item>
/// <description>
/// <c>[assembly: AssemblyMetadata( "PackageBuildDate", … )]</c>, in a format
/// <see cref="DateTime.Parse(string, IFormatProvider)"/> reads under the invariant culture. This one is not
/// decorative: the end date of a subscription is compared against the build date, and a licence is refused with
/// an exception when the application cannot say when it was built, so an absent attribute turns every licensed
/// build of the product into a crash.
/// </description>
/// </item>
/// <item>
/// <description>
/// <c>[assembly: AssemblyCompany( … )]</c> equal to <see cref="ProductProfile.Company"/>. Licensing finds the most
/// recently built component of the vendor by filtering the components of the application on that string, so a
/// company that does not match makes the search find nothing and the subscription date is compared against the
/// wrong component.
/// </description>
/// </item>
/// </list>
/// <para>
/// The environment variables named below override two of these for testing. They are named after the product, so
/// <c>METALAMA_BUILD_DATE</c> and <c>POSTSHARP_BUILD_DATE</c> are different variables.
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