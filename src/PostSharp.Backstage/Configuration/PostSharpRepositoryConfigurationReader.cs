// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Repositories;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace PostSharp.Backstage.Configuration;

/// <summary>
/// Reads the repository settings of PostSharp from the <c>postsharp.config</c> file at the root of a repository, where
/// a repository opts out of telemetry with <c>&lt;Property Name="TelemetryEnabled" Value="False" /&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// The file is the configuration file that the compiler already reads in every directory above a project, so it is
/// also used below the root, where it is reported only when it declares <c>TelemetryEnabled</c>.
/// </para>
/// <para>
/// The property is read here, without the compiler, so it has none of the features of a property of the compiler:
/// the value must be the literal <c>True</c> or <c>False</c>, in any casing, and the property must have no attribute
/// other than <c>Name</c> and <c>Value</c>. An expression, a condition or a second definition would be evaluated by
/// the compiler and not here, so it is rejected and reported rather than read with a meaning the user did not intend.
/// </para>
/// </remarks>
internal sealed class PostSharpRepositoryConfigurationReader : IRepositoryConfigurationReader
{
    /// <summary>
    /// The name of the property that enables or disables telemetry for the repository.
    /// </summary>
    public const string TelemetryEnabledPropertyName = "TelemetryEnabled";

    private static readonly XNamespace _configurationNamespace = "http://schemas.postsharp.org/1.0/configuration";

    private PostSharpRepositoryConfigurationReader() { }

    public static PostSharpRepositoryConfigurationReader Instance { get; } = new();

    public bool IsFileUsedBelowRoot => true;

    public RepositoryConfigurationReadResult Read( string filePath, string text )
    {
        XDocument document;

        try
        {
            document = XDocument.Parse( text );
        }
        catch ( XmlException e )
        {
            return Error( false, $"it is not well-formed XML: {e.Message}" );
        }

        var root = document.Root!;

        if ( root.Name != _configurationNamespace + "Project" )
        {
            return Error(
                false,
                $"its root element is '{root.Name.LocalName}' and not the 'Project' element of the namespace '{_configurationNamespace.NamespaceName}'." );
        }

        var properties = root.Elements( _configurationNamespace + "Property" )
            .Where( p => (string?) p.Attribute( "Name" ) == TelemetryEnabledPropertyName )
            .ToList();

        switch ( properties.Count )
        {
            case 0:
                return new RepositoryConfigurationReadResult();

            case > 1:
                return Error( true, $"it defines the '{TelemetryEnabledPropertyName}' property {properties.Count} times, and the property can be defined only once." );
        }

        var property = properties[0];

        var unsupportedAttribute = property.Attributes().FirstOrDefault( a => a.Name != "Name" && a.Name != "Value" );

        if ( unsupportedAttribute != null )
        {
            return Error(
                true,
                $"the '{TelemetryEnabledPropertyName}' property has the '{unsupportedAttribute.Name.LocalName}' attribute, and only the 'Name' and 'Value' attributes are supported for this property." );
        }

        var value = (string?) property.Attribute( "Value" );

        if ( string.Equals( value, "true", StringComparison.OrdinalIgnoreCase ) )
        {
            return new RepositoryConfigurationReadResult { TelemetryEnabled = true, DeclaresSettings = true };
        }

        if ( string.Equals( value, "false", StringComparison.OrdinalIgnoreCase ) )
        {
            return new RepositoryConfigurationReadResult { TelemetryEnabled = false, DeclaresSettings = true };
        }

        if ( value == null )
        {
            return Error( true, $"the '{TelemetryEnabledPropertyName}' property has no 'Value' attribute." );
        }

        if ( value.IndexOf( '{' ) >= 0 )
        {
            return Error(
                true,
                $"the value of the '{TelemetryEnabledPropertyName}' property, '{value}', is an expression. Expressions are not supported for this property: the value must be 'True' or 'False'." );
        }

        return Error(
            true,
            $"the value of the '{TelemetryEnabledPropertyName}' property, '{value}', is not supported: the value must be 'True' or 'False'." );
    }

    private static RepositoryConfigurationReadResult Error( bool declaresSettings, string error )
        => new() { DeclaresSettings = declaresSettings, Errors = ImmutableArray.Create( error ) };
}
