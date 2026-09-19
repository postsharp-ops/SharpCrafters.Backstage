// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Licensing;

namespace SharpCrafters.Backstage.Worker.Pages.Shared;

internal static class GlobalState
{
    public static SelectedAction SelectedAction { get; set; }

    public static string? LicenseKey { get; set; }

    /// <summary>
    /// The alias of the edition that the user chose, when the choice was one of those the product family offers.
    /// </summary>
    public static string? SelfRegisteredEditionAlias { get; set; }

    public static CommunityLicenseReason CommunityLicenseReason { get; set; }
}