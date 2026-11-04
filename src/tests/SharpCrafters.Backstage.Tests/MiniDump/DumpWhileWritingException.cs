// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;

namespace SharpCrafters.Backstage.Tests.MiniDump;

/// <summary>
/// The exception that the first step of a dump throws in <see cref="MiniDumpTests.AnExceptionWhileWritingDoesNotStartAnotherDump"/>,
/// which is the only exception type that the dumper of that test writes a dump of.
/// </summary>
internal sealed class DumpWhileWritingException : Exception;
