// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

namespace SharpCrafters.Aspects.Introspection;

/// <summary>
/// The names of the elements and attributes of version 1, the <c>.pssym</c> file of PostSharp.
/// </summary>
internal static class V1Names
{
    public const string Namespace = "http://schemas.postsharp.org/2.0/symbols";

    public const int SchemaVersion = 1;

    // The value of an attribute that goes through the name table: #N=<string> at its first use, #N at the next ones.
    public const char NameTablePrefix = '#';

    public const string Symbols = "Symbols";
    public const string Evolution = "Evolution";
    public const string Class = "Class";
    public const string Instance = "Instance";
    public const string Target = "Target";
    public const string JoinPoint = "JoinPoint";
    public const string Annotation = "Annotation";

    // Symbols
    public const string LinesOfCode = "LinesOfCode";
    public const string PostSharpLinesOfCode = "PostSharpLinesOfCode";
    public const string EnhancedTypesCount = "EnhancedTypesCount";
    public const string SchemaVersionAttribute = "SchemaVersion";
    public const string PostSharpVersion = "PostSharpVersion";

    // Evolution
    public const string Source = "Source";
    public const string Kind = "Kind";

    // Class
    public const string LimitedLicense = "LimitedLicense";

    // Instance
    public const string Declaration = "Declaration";
    public const string File = "File";
    public const string Line = "Line";
    public const string Column = "Column";
    public const string ForwardRef = "ForwardRef";
    public const string Description = "Description";
    public const string Id = "Id";
    public const string Origin = "Origin";
    public const string ProvidedBy = "ProvidedBy";

    // Target (also an attribute of Target, Evolution and Annotation)
    public const string LinesAvoided = "LinesAvoided";
    public const string Tags = "Tags";

    // JoinPoint
    public const string Advised = "Advised";
    public const string Advising = "Advising";
    public const string Semantic = "Semantic";
    public const string Ordinal = "Ordinal";

    // Annotation
    public const string Arguments = "Arguments";

    public const string True = "true";

    // The order of a transformation when its Ordinal attribute is absent.
    public const int ImpliedOrder = 1;
}
