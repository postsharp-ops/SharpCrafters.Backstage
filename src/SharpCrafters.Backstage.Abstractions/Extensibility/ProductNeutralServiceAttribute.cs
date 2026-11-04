// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

namespace SharpCrafters.Backstage.Extensibility;

/// <summary>
/// Marks a service type whose instance does not depend on the product that hosts the Backstage services, so that the
/// service providers of several products in one process can use the same instance.
/// </summary>
/// <remarks>
/// <para>
/// A composite product (see <c>BackstageProduct.CreateComposite</c>) creates an inner service provider for each of its
/// secondary products. When the inner provider registers a service type that carries this attribute, and the outer
/// provider has an instance of that type, the inner provider uses the instance of the outer provider instead of
/// creating its own.
/// </para>
/// <para>
/// The attribute is set on the type under which the service is registered, which is usually an interface. A service
/// depends on the product when it reads the product profile, for instance to name a directory, a lock or a
/// configuration store after the product. Such a service must not carry the attribute.
/// </para>
/// </remarks>
[AttributeUsage( AttributeTargets.Interface | AttributeTargets.Class, Inherited = false )]
public sealed class ProductNeutralServiceAttribute : Attribute;
