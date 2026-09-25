// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Xunit;

// The tests change the environment of the process, such as PATH, and end processes of the machine, so they run one at a time.
[assembly: CollectionBehavior( DisableTestParallelization = true )]
