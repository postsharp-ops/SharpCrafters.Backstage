# Schema version 2 of the weaving symbol file (`.mlsym`)

Schema version 2 is the format of the weaving symbol file that Metalama writes. The file has the extension `.mlsym`. By
default, it is written next to the output assembly when the MSBuild property `MetalamaWriteIntrospectionFile` is `true`.
The `introspect` commands of `dotnet metalama` read it.

The general design is in `Documentation/future/Introspection-Cli.md` of the PostSharp repository, sections 9 and 10.3.
Version 1 is the XML file of PostSharp (`.pssym`). Version 2 does not stay faithful to it.

## Format

The file is one JSON document in UTF-8. It is written as a stream by `IntrospectionV2Writer` and read forward only by
`IntrospectionV2Reader`, both in the package `SharpCrafters.Aspects.Introspection`.

The format has no name table. Each declaration is written as a string at every use, so that a person or an agent can
search the file as text. Metalama writes the file indented, and it does not escape the characters that are safe in a
file, such as `<` in a generic type name.

## Structure

```json
{
  "schemaVersion": 2,
  "product": "Metalama",
  "productVersion": "2027.0.8",
  "assembly": "Sample",
  "targetFramework": "net10.0",
  "compilationSucceeded": true,
  "aspectClasses": [
    {
      "id": "T:Sample.LogAttribute",
      "displayName": "Log",
      "description": "Writes a message to the console when a method starts.",
      "layers": [ {}, { "name": "Build", "order": 1 } ],
      "aspectInstances": [
        {
          "id": 1,
          "targetDeclaration": "M:Sample.Calculator.Add(System.Int32,System.Int32)",
          "location": { "file": "Calculator.cs", "line": 8, "column": 5 },
          "predecessors": [
            { "kind": "Attribute", "declaration": "M:Sample.Calculator.Add(System.Int32,System.Int32)", "attributeClass": "T:Sample.LogAttribute" }
          ],
          "transformations": [
            { "kind": "OverrideMember", "adviceKind": "OverrideMethod", "order": 1, "targetDeclaration": "M:Sample.Calculator.Add(System.Int32,System.Int32)" }
          ],
          "diagnostics": [
            { "id": "LOG001", "severity": "Warning", "message": "The method cannot be logged." }
          ]
        }
      ]
    }
  ]
}
```

An aspect class contains its aspect layers, then its aspect instances. An aspect instance contains its predecessors, then
its transformations, then its diagnostics. The properties of an object come before its lists.

## Properties

### Root object

| Property | Type | Required | Description |
|---|---|---|---|
| `schemaVersion` | integer | yes | `2`. A reader rejects a lower version. It reads a higher version with the rules of version 2. |
| `product` | string | yes | The product that wrote the file, `Metalama`. |
| `productVersion` | string | no | The version of the product. |
| `assembly` | string | no | The name of the assembly that the compilation produces. An identifier without an assembly qualifier is a declaration of this assembly. |
| `targetFramework` | string | no | The target framework of the compilation. |
| `compilationSucceeded` | Boolean | no | Whether the compilation succeeded. A file of a compilation that failed can be incomplete. The implied value is `true`. |
| `aspectClasses` | array | no | The aspect classes. |

### Aspect class

| Property | Type | Required | Description |
|---|---|---|---|
| `id` | identifier | yes | The type of the aspect class. |
| `displayName` | string | no | The name that the user interface shows. |
| `description` | string | no | The description of the aspect class. |
| `layers` | array | no | The aspect layers. Each one has a `name`, absent for the default layer, and an `order` among all the layers of the compilation, implied `0`. |
| `aspectInstances` | array | no | The aspect instances. |

### Aspect instance

| Property | Type | Required | Description |
|---|---|---|---|
| `id` | integer | yes | A positive number, unique in the file. It is not stable between two builds. The query layer computes stable identifiers from structural keys. |
| `targetDeclaration` | identifier | yes | The declaration to which the aspect is applied. |
| `location` | location | no | The location of the target declaration. |
| `isSkipped` | Boolean | no | Whether the aspect instance was skipped. The implied value is `false`. |
| `predecessors` | array | no | What created the aspect instance. |
| `transformations` | array | no | The transformations, in the order of the aspect instance. |
| `diagnostics` | array | no | The diagnostics that the aspect instance reported. |

### Predecessor

A predecessor is a child of the aspect instance that it created. A predecessor that created several aspect instances,
such as a fabric, is repeated in each of them. A reader treats the repeated predecessors as one.

| Property | Type | Required | Description |
|---|---|---|---|
| `kind` | string | yes | `Attribute`, `ChildAspect`, `RequiredAspect`, `Inherited`, `Fabric` or `Interactive`. A reader rejects another value. |
| `degree` | integer | no | The number of aspect instances between the predecessor and the source code. The implied value is `0`. |
| `declaration` | identifier | no | For `Attribute`, the declaration that carries the custom attribute. For `Inherited`, the base declaration. For `Fabric`, the type of the fabric. |
| `attributeClass` | identifier | no | For `Attribute`, the type of the custom attribute. |
| `aspectInstance` | integer | no | For `ChildAspect` and `RequiredAspect`, the `id` of the other aspect instance. It can be an aspect instance that comes later in the file. |
| `location` | location | no | The location of the predecessor, such as the position of the custom attribute. |

### Transformation

| Property | Type | Required | Description |
|---|---|---|---|
| `kind` | string | yes | A member of `IntrospectionTransformationKind`, such as `OverrideMember` or `IntroduceMember`. It is a string, so that a reader accepts a kind that a later version adds. |
| `adviceKind` | string | no | A member of `AdviceKind`, such as `OverrideMethod`. |
| `layer` | string | no | The aspect layer of the advice. It is absent for the default layer. |
| `order` | integer | no | The order of the transformation within the aspect instance. The implied value is `0`. |
| `targetDeclaration` | identifier | yes | The declaration that the transformation changes. |
| `introducedDeclaration` | identifier | no | The member, parameter or interface that the transformation introduces. |
| `description` | string | no | A description for a person. |

### Diagnostic

| Property | Type | Required | Description |
|---|---|---|---|
| `id` | string | yes | The identifier of the diagnostic, such as `LAMA0041`. |
| `severity` | string | yes | `Hidden`, `Info`, `Warning` or `Error`. |
| `message` | string | no | The message. |
| `declaration` | identifier | no | The declaration on which the diagnostic is reported. |
| `location` | location | no | The location of the diagnostic. |

### Location

| Property | Type | Required | Description |
|---|---|---|---|
| `file` | string | yes | The path of the source file. |
| `line` | integer | no | The line, from 1. |
| `column` | integer | no | The column, from 1. It is written only with `line`. |
| `endLine` | integer | no | The last line of the declaration that starts at this location, for the location of a target declaration. A reader uses it to find the declarations that contain a line. It is written only with `line`. |

## Identifiers

An identifier is a `SerializableDeclarationId` of Metalama: a documentation comment identifier of Roslyn, with these
additions:

- A parameter is the identifier of its method followed by `;Parameter=` and its position, such as
  `M:App.C.M(System.Int32);Parameter=0`. The return parameter is followed by `;Return`.
- Other suffixes, such as `;TypeParameter=`, `;File=` and `;Nullable`, are kept in the identifier.
- An assembly is `Assembly:` followed by its identity.
- A declaration of another assembly than the one of the file is preceded by the name of its assembly in brackets, such
  as `[Lib]T:Lib.BaseEntity`.

## Compatibility rules

- A writer omits a property whose value is the implied value (`false`, `0`), and an optional property that has no value.
- A reader ignores the properties that it does not know, including unknown lists, so that a file written by a later
  version of the product with the same schema version remains readable.
- A reader ignores a property of an aspect class or of an aspect instance that comes after one of its lists.
- A reader accepts JSON comments and a UTF-8 byte order mark.
- Not yet decided: whether to write the secondary instances of an aspect instance, and the path of the transformed
  file when `MetalamaEmitCompilerTransformedFiles` is set. A reader of version 2 ignores both if a later writer adds them.
