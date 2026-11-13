// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

namespace SharpCrafters.Aspects.Introspection.V2;

/// <summary>
/// The start of an aspect instance, inside an aspect class, in a file of schema version 2. It contains the predecessors, then the
/// transformations, then the diagnostics.
/// </summary>
public readonly struct V2AspectInstanceData
{
    /// <summary>
    /// Gets the identifier of the aspect instance in the file (<c>id</c>). It is a positive number that is unique in the file, and
    /// it is not stable between two builds. A predecessor of kind <see cref="V2PredecessorKind.ChildAspect"/> or
    /// <see cref="V2PredecessorKind.RequiredAspect"/> refers to it.
    /// </summary>
    public required int Id { get; init; }

    /// <summary>
    /// Gets the declaration to which the aspect instance is applied (<c>targetDeclaration</c>).
    /// </summary>
    public required DeclarationHandle TargetDeclaration { get; init; }

    /// <summary>
    /// Gets the source location of the target declaration (<c>location</c>), or <c>null</c>.
    /// </summary>
    public V2SourceLocation? Location { get; init; }

    /// <summary>
    /// Gets a value indicating whether the aspect instance was skipped (<c>isSkipped</c>), because of its eligibility or because
    /// its <c>BuildAspect</c> method skipped it. The implied value is <c>false</c>.
    /// </summary>
    public bool IsSkipped { get; init; }
}
