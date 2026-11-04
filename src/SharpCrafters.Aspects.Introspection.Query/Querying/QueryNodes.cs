// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Microsoft.OData.UriParser;

namespace SharpCrafters.Aspects.Introspection.Query.Querying;

/// <summary>
/// Helpers over the nodes of a query that Microsoft.OData.Core binds.
/// </summary>
internal static class QueryNodes
{
    // The name of the range variable of the object that a condition tests.
    public const string ImplicitVariable = "$it";

    public static SingleValueNode StripConvert( SingleValueNode node ) => node is ConvertNode convert ? StripConvert( convert.Source ) : node;

    /// <summary>
    /// Describes a node as the path that the query writes, such as <c>p/kind</c>, for an error message.
    /// </summary>
    public static string Describe( QueryNode node )
        => node switch
        {
            ConvertNode convert => Describe( convert.Source ),
            ResourceRangeVariableReferenceNode variable => variable.Name == ImplicitVariable ? "" : variable.Name,
            NonResourceRangeVariableReferenceNode variable => variable.Name == ImplicitVariable ? "" : variable.Name,
            SingleValuePropertyAccessNode property => Prefix( property.Source ) + property.Property.Name,
            CollectionPropertyAccessNode property => Prefix( property.Source ) + property.Property.Name,
            SingleNavigationNode navigation => Prefix( navigation.Source ) + navigation.NavigationProperty.Name,
            CollectionNavigationNode navigation => Prefix( navigation.Source ) + navigation.NavigationProperty.Name,
            CountNode count => Prefix( count.Source ) + "$count",
            SingleValueFunctionCallNode function => function.Name + "(...)",
            ConstantNode constant => constant.LiteralText ?? "null",
            _ => node.Kind.ToString()
        };

    private static string Prefix( QueryNode source )
    {
        var prefix = Describe( source );

        return prefix.Length == 0 ? "" : prefix + "/";
    }
}
