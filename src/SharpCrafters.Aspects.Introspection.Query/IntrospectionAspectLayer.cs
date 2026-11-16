// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

namespace SharpCrafters.Aspects.Introspection.Query;

/// <summary>
/// An aspect layer of an aspect class (Metalama).
/// </summary>
/// <param name="Name">The name of the aspect layer, or <c>null</c> for the default aspect layer.</param>
/// <param name="Order">The order of execution of the aspect layer among all the aspect layers of the compilation.</param>
public sealed record IntrospectionAspectLayer( string? Name, int Order );
