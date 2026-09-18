// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Licensing.Consumption;

namespace PostSharp.Backstage;

/// <summary>
/// The requirements that PostSharp asks for. There is one per package, because a requirement names exactly one.
/// </summary>
/// <remarks>
/// Which of these a given aspect asks for is decided by the assembly that declares it, and that decision belongs to
/// the compiler. What belongs here is what each requirement means, so that the answer is the same wherever the
/// question is asked.
/// </remarks>
[PublicAPI]
public static class PostSharpLicenseRequirements
{
    /// <summary>
    /// Gets the requirement of the free edition, which every valid license satisfies. A project is checked against
    /// it once, so that a user of the free edition still has to hold a license.
    /// </summary>
    public static LicenseRequirement Essentials { get; } = new PostSharpLicenseRequirement( "PostSharp Essentials", LicensedPackages.Essentials );

    /// <summary>
    /// Gets the requirement of an aspect written by the user, or by a library that is not one of the pattern
    /// libraries of PostSharp.
    /// </summary>
    public static LicenseRequirement Framework { get; } = new PostSharpLicenseRequirement( "PostSharp Framework", LicensedPackages.Framework );

    /// <summary>
    /// Gets the requirement of the aspects of the PostSharp Patterns Common library, on which the other pattern
    /// libraries rest.
    /// </summary>
    public static LicenseRequirement Common { get; } = new PostSharpLicenseRequirement( "PostSharp Patterns Common", LicensedPackages.Common );

    /// <summary>
    /// Gets the requirement of the aspects of the PostSharp Aggregation library.
    /// </summary>
    public static LicenseRequirement Aggregatable { get; } = new PostSharpLicenseRequirement( "PostSharp Aggregation", LicensedPackages.Aggregatable );

    /// <summary>
    /// Gets the requirement of the aspects of the PostSharp Threading library.
    /// </summary>
    public static LicenseRequirement Threading { get; } = new PostSharpLicenseRequirement( "PostSharp Threading", LicensedPackages.Threading );

    /// <summary>
    /// Gets the requirement of the aspects of the PostSharp MVVM library, other than change notification on
    /// automatic properties, which the free edition grants.
    /// </summary>
    public static LicenseRequirement Model { get; } = new PostSharpLicenseRequirement( "PostSharp MVVM", LicensedPackages.Model );

    /// <summary>
    /// Gets the requirement of the aspects of the PostSharp XAML library.
    /// </summary>
    public static LicenseRequirement Xaml { get; } = new PostSharpLicenseRequirement( "PostSharp XAML", LicensedPackages.Xaml );

    /// <summary>
    /// Gets the requirement of the aspects of the PostSharp Logging library.
    /// </summary>
    public static LicenseRequirement Diagnostics { get; } = new PostSharpLicenseRequirement( "PostSharp Logging", LicensedPackages.Diagnostics );

    /// <summary>
    /// Gets the requirement of the aspects of the PostSharp Caching library.
    /// </summary>
    public static LicenseRequirement Caching { get; } = new PostSharpLicenseRequirement( "PostSharp Caching", LicensedPackages.Caching );
}
