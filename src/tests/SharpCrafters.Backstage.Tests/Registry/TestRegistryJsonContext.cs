// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System.Text.Json.Serialization;

namespace SharpCrafters.Backstage.Tests.Registry;

/// <summary>
/// Makes the configuration object of these tests serializable, as every configuration object of the product is.
/// </summary>
/// <remarks>
/// The manager compares two objects by their JSON, so a type it holds has to be one the serializer knows. Every real
/// configuration object is declared in the context of the product; this one is declared here because it exists only
/// for the tests, and leaving it out would have them exercise a manager that cannot compare what it is given.
/// </remarks>
[JsonSerializable( typeof(TestRegistryConfiguration) )]
internal sealed partial class TestRegistryJsonContext : JsonSerializerContext;
