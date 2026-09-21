// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;

namespace PostSharp.Backstage.Licensing;

/// <summary>
/// The sets of packages that a product grants, and the test by which a set satisfies a requirement.
/// </summary>
[PublicAPI]
public static class LicensedPackageSets
{
    /// <summary>
    /// What the free edition grants.
    /// </summary>
    public const LicensedPackages Essentials = LicensedPackages.Essentials | LicensedPackages.Common;

    /// <summary>
    /// What PostSharp MVVM grants.
    /// </summary>
    public const LicensedPackages Mvvm = Essentials | LicensedPackages.Model | LicensedPackages.Xaml | LicensedPackages.Aggregatable;

    /// <summary>
    /// What PostSharp Threading grants.
    /// </summary>
    public const LicensedPackages Threading = Essentials | LicensedPackages.Threading | LicensedPackages.Aggregatable;

    /// <summary>
    /// What PostSharp Logging grants.
    /// </summary>
    public const LicensedPackages Logging = Essentials | LicensedPackages.Diagnostics;

    /// <summary>
    /// What PostSharp Caching grants.
    /// </summary>
    public const LicensedPackages Caching = Essentials | LicensedPackages.Caching;

    /// <summary>
    /// What PostSharp Framework grants.
    /// </summary>
    public const LicensedPackages Framework = Essentials | LicensedPackages.Framework | LicensedPackages.Diagnostics;

    /// <summary>
    /// What PostSharp Ultimate grants.
    /// </summary>
    public const LicensedPackages Ultimate = LicensedPackages.All;

    /// <summary>
    /// What an unattended build is entitled to. It is everything but <see cref="LicensedPackages.Diagnostics"/>,
    /// which PostSharp Logging is licensed per production server rather than per build.
    /// </summary>
    public const LicensedPackages Unattended = LicensedPackages.All & ~LicensedPackages.Diagnostics;

    /// <summary>
    /// Determines whether a set of granted packages satisfies a requirement, which it does when it contains every
    /// package the requirement names.
    /// </summary>
    public static bool Includes( this LicensedPackages grantedPackages, LicensedPackages requirement )
        => (grantedPackages & requirement) == requirement;
}
