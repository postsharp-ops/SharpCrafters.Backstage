// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Extensibility;

namespace SharpCrafters.Backstage.UserInterface;

// This service is intentionally separate from IUnattendedProcessDetector, so that the enforcement of licensing does not
// depend on variable factors such as the last user input or the size of the monitor.
internal interface IUserDeviceDetectionService : IBackstageService
{
    bool IsInteractiveDevice { get; }

    bool? IsVisualStudioInstalled { get; }
}