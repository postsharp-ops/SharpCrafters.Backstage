// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace PostSharp.Backstage.Serialization;

/// <summary>
/// The configuration objects that belong to PostSharp rather than to the product-neutral services.
/// </summary>
/// <remarks>
/// <para>
/// A host passes <see cref="Default"/> in
/// <c>BackstageInitializationOptions.AdditionalJsonTypeInfoResolvers</c>. Without it the serializer does not know
/// these types, and the first read of one of them fails at run time rather than at build time, because the types are
/// reached through the configuration manager and not by name.
/// </para>
/// <para>
/// The dictionary types are declared as well as the immutable ones, because the converter that reads an
/// <see cref="ImmutableDictionary{TKey,TValue}"/> deserializes a <see cref="Dictionary{TKey,TValue}"/> first.
/// </para>
/// </remarks>
[JsonSourceGenerationOptions( WriteIndented = true )]
[JsonSerializable( typeof(PostSharpEssentialsUsageConfiguration) )]
[JsonSerializable( typeof(ImmutableDictionary<string, int>) )]
[JsonSerializable( typeof(Dictionary<string, int>) )]
[PublicAPI]
public sealed partial class PostSharpBackstageJsonContext : JsonSerializerContext;
