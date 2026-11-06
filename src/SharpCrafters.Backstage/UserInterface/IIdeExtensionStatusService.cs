// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Extensibility;

namespace SharpCrafters.Backstage.UserInterface;

/// <summary>
/// Reads and writes whether the IDE extension is installed. One extension serves every product, so the status is the
/// same for all of them.
/// </summary>
[ProductNeutralService]
public interface IIdeExtensionStatusService : IBackstageService
{
    bool ShouldRecommendToInstallVisualStudioExtension { get; }

    bool IsVisualStudioExtensionInstalled { get; set; }
}