// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using System;

namespace PostSharp.Backstage;

/// <summary>
/// The packages of PostSharp that a license grants, and that a feature requires.
/// </summary>
/// <remarks>
/// <para>
/// This is where PostSharp licensing differs from Metalama licensing in kind and not only in detail. A Metalama
/// license entitles a feature when its product is one of those the feature names. A PostSharp license grants a set
/// of packages, derived from its product and its license type, and a feature requires one package; the license
/// entitles the feature when the set contains it. One key can therefore entitle some features of a build and not
/// others, which no Metalama key does.
/// </para>
/// <para>
/// The values are those of <c>LicensedPackages</c> in PostSharp 2026.0 and must not change: they are the meaning of
/// the license keys that this version consumes.
/// </para>
/// </remarks>
[PublicAPI]
[Flags]
public enum LicensedPackages
{
    None = 0,

    /// <summary>
    /// The free edition, PostSharp Essentials. Every valid license grants it.
    /// </summary>
    Essentials = 1,

    /// <summary>
    /// The aspects of <c>PostSharp.Patterns.Common</c>, on which the other pattern libraries rest.
    /// </summary>
    Common = 2,

    /// <summary>
    /// Aspects written by the user, and by any library that is not one of the pattern libraries.
    /// </summary>
    Framework = 4,

    /// <summary>
    /// The aspects of <c>PostSharp.Patterns.Threading</c>.
    /// </summary>
    Threading = 8,

    /// <summary>
    /// The aspects of <c>PostSharp.Patterns.Model</c>, other than change notification on automatic properties,
    /// which is granted by <see cref="Essentials"/>.
    /// </summary>
    Model = 16,

    /// <summary>
    /// The aspects of <c>PostSharp.Patterns.Xaml</c>.
    /// </summary>
    Xaml = 32,

    /// <summary>
    /// The aspects of <c>PostSharp.Patterns.Aggregation</c>.
    /// </summary>
    Aggregatable = 64,

    /// <summary>
    /// The aspects of <c>PostSharp.Patterns.Diagnostics</c>, that is PostSharp Logging.
    /// </summary>
    Diagnostics = 128,

    /// <summary>
    /// The aspects of <c>PostSharp.Patterns.Caching</c>.
    /// </summary>
    Caching = 256,

    /// <summary>
    /// Every package.
    /// </summary>
    All = Essentials | Common | Framework | Aggregatable | Threading | Caching | Model | Xaml | Diagnostics
}

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
