The `SharpCrafters.Backstage` package is not meant to be directly referenced in user projects.

It contains the services of Backstage, the infrastructure of the Metalama and PostSharp products, that do not depend on a product: dependency injection, diagnostics, the file system, named locks, temporary files, configuration, telemetry, licensing and the user interface. The values and implementations that are specific to a product are in a customization package that depends on this one: [Metalama.Backstage](https://www.nuget.org/packages/Metalama.Backstage) for Metalama.
