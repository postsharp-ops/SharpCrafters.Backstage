// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System.Collections.Generic;

namespace SharpCrafters.Aspects.Introspection.Query;

/// <summary>
/// One aspect on one target declaration, with its transformations.
/// </summary>
public sealed class IntrospectionAspectInstance
{
    internal IntrospectionAspectInstance( IntrospectionAspectClass aspectClass, IntrospectionDeclaration targetDeclaration )
    {
        this.AspectClass = aspectClass;
        this.TargetDeclaration = targetDeclaration;
    }

    /// <summary>
    /// Gets the identifier of the aspect instance: the first 48 bits of the hash of <see cref="StructuralKey"/>, as 12 lowercase
    /// hexadecimal characters. It stays the same across builds as long as the source does not change.
    /// </summary>
    public string Id { get; internal set; } = "";

    /// <summary>
    /// Gets the key from which <see cref="Id"/> is computed, such as <c>App.LogAttribute@[App]M:App.Repository.Get(System.Int32)</c>.
    /// </summary>
    public string StructuralKey { get; internal set; } = "";

    /// <summary>
    /// Gets the aspect class.
    /// </summary>
    public IntrospectionAspectClass AspectClass { get; }

    /// <summary>
    /// Gets the declaration to which the aspect instance is applied.
    /// </summary>
    public IntrospectionDeclaration TargetDeclaration { get; }

    /// <summary>
    /// Gets what created the aspect instance. A file of version 1 gives one predecessor.
    /// </summary>
    public IReadOnlyList<IntrospectionPredecessor> Predecessors => this.PredecessorsList;

    /// <summary>
    /// Gets the transformations of the aspect instance, in the order of the file.
    /// </summary>
    public IReadOnlyList<IntrospectionTransformation> Transformations => this.TransformationsList;

    /// <summary>
    /// Gets the number of lines of code that the aspect instance saves.
    /// </summary>
    public int LinesOfCodeAvoided { get; internal set; }

    /// <summary>
    /// Gets the tags of the aspect instance, such as <c>premium</c> for an aspect instance that the licence does not cover, or
    /// <c>null</c>.
    /// </summary>
    public string? Tags { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether the aspect instance was skipped (Metalama). A file of version 1 records no skipped aspect
    /// instance.
    /// </summary>
    public bool IsSkipped { get; internal set; }

    internal List<IntrospectionPredecessor> PredecessorsList { get; } = [];

    internal List<IntrospectionTransformation> TransformationsList { get; } = [];

    // The identifier that the file gives the aspect instance (Target/@Id of version 1), which the predecessors of the aspect
    // instances that it provides refer to.
    internal string? FileId { get; set; }

    /// <inheritdoc />
    public override string ToString() => this.StructuralKey;
}
