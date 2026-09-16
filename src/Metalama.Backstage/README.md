![Metalama by PostSharp](https://raw.githubusercontent.com/metalama/.github/HEAD/images/metalama.svg)

The `Metalama.Backstage` package is not meant to be directly referenced in user projects.

It is the Metalama customization of Backstage, the infrastructure of the Metalama and PostSharp products whose services are in the [SharpCrafters.Backstage](https://www.nuget.org/packages/SharpCrafters.Backstage) package: the product profile, the web links, the license catalog and requirements, and the telemetry key. Metalama uses it to process license keys, telemetry and other configuration settings:

* [Metalama Command Line Tools](https://www.nuget.org/packages/Metalama.Tool) uses this package to handle commands.
* [Visual Studio Tools for Metalama and PostSharp](https://marketplace.visualstudio.com/items?itemName=PostSharpTechnologies.PostSharp) uses this package to manage options and license registration.
