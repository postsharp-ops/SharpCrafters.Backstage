// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;
using System.Collections.Generic;

namespace SharpCrafters.Aspects.Introspection.Query.Projects;

/// <summary>
/// What the evaluation of a project gives for one of its target frameworks.
/// </summary>
internal sealed class EvaluatedTarget
{
    public string TargetFramework { get; set; } = "";

    public string Configuration { get; set; } = "";

    public string AssemblyName { get; set; } = "";

    public string FilePath { get; set; } = "";

    public Dictionary<string, string> AdditionalFiles { get; set; } = new( StringComparer.OrdinalIgnoreCase );

    // The compiled source files, outside of obj, whose write times tell whether the file is stale.
    public List<string> SourceFiles { get; set; } = [];
}
