// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;

namespace SharpCrafters.Backstage.Licensing.Consumption;

/// <summary>
/// A license key or license server URL that the application supplies for one project, together with a description of
/// where the application read it from.
/// </summary>
/// <param name="LicenseString">The license key or the URL of a license server.</param>
/// <param name="SourceDescription">
/// Where the application read <paramref name="LicenseString"/> from, in a form that can be read inside a sentence, such
/// as "the LicenseKey property of the file 'x.config'". A message about an unusable license names this description
/// instead of quoting the license string, because the string may be a secret of a continuous integration server and
/// must not reach a build log.
/// </param>
/// <remarks>
/// An application that reads its licenses from one place only has <see cref="LicenseConsumptionOptions.ProjectLicenseKey"/>,
/// which describes itself as the license property of the product. This type is for an application that has several such
/// places and wants each message to name the one it came from.
/// </remarks>
[PublicAPI]
public sealed record ExplicitLicense( string LicenseString, string SourceDescription );
