# SharpCrafters.Backstage

Infrastructure services shared by the Metalama and PostSharp products: configuration, diagnostics, telemetry,
licensing and user interface, with a customization package per product.

The product name inside the build system and on TeamCity is `Backstage`. The repository is named
`SharpCrafters.Backstage`, the neutral packages `SharpCrafters.Backstage*` and `SharpCrafters.Common`, and the product
customizations `Metalama.Backstage*` and, later, `PostSharp.Backstage*`.

## Packages

| Package | Contents |
|---|---|
| `SharpCrafters.Common.Abstractions` | The interfaces of the hooks that production code exposes for deterministic testing. It has no dependency |
| `SharpCrafters.Common` | The implementations of those hooks, which tests register. It depends on `SharpCrafters.Common.Abstractions` |
| `SharpCrafters.Backstage.Abstractions` | The contracts that the packages below share: the logger, the service marker interface and the environment variables. It has no dependency |
| `SharpCrafters.Backstage.Threading` | Named locks. It depends on the two abstractions packages |
| `SharpCrafters.Backstage.FileLocks` | The retry of file operations and the detection of the processes holding a file. It depends on `SharpCrafters.Backstage.Abstractions` |
| `SharpCrafters.Backstage.ProcessClassification` | The process kinds, the detection of a container and the launch of a debugger. It depends on `SharpCrafters.Backstage.Abstractions` |
| `SharpCrafters.Backstage` | The services: dependency injection, logging, configuration, the file system, the detection of an unattended process and of a continuous integration server, telemetry, licensing and user interface |
| `SharpCrafters.Backstage.Commands` | The command line over those services |
| `SharpCrafters.Backstage.Profiling` | The optional profiling feature, which needs `JetBrains.Profiler.SelfApi`. No product references it |
| `SharpCrafters.Aspects.Introspection` | The forward-only reader and writer of the weaving symbol file of PostSharp (`.pssym`, schema version 1), which the PostSharp compiler writes through. It has no dependency, and no other package depends on it but the next one. The design is `Documentation/future/Introspection-Cli.md` of PostSharp, section 7.2 |
| `SharpCrafters.Aspects.Introspection.Query` | The common model of the introspection of aspects (aspect classes, aspect instances, predecessors, transformations, annotations, declaration mappings, declarations), its loader from schema version 1, the conversion of the names of version 1 to documentation comment identifiers, the structural keys and 48-bit identifiers of the aspect instances and predecessors, the evaluation of the projects with the MSBuild of a .NET SDK, the OData queries of `--where` and `--order-by`, parsed by Microsoft.OData.Core and evaluated against the model, the merge of the files of the projects of a solution and their target frameworks, the caches of the evaluations and of the files (in `SharpCrafters/Introspection` of the local application data), and the `introspect` commands (`list`, `show`, `schema`, and `batch`, which runs several of them, one per line of the standard input or of a file, over one load of the files), which a tool adds with `IntrospectionCommandFactory.AddIntrospectBranch` and an `IntrospectionProvider` of its product (same document, sections 5 to 7). It depends on `SharpCrafters.Aspects.Introspection`, Spectre.Console, `Microsoft.OData.Core` and `Microsoft.Build.Locator`; the MSBuild assemblies come from the .NET SDK |
| `Metalama.Backstage`, `PostSharp.Backstage` | What each product family adds: its profile, its licence catalogue, its web links and its telemetry endpoints |
| `SharpCrafters.Backstage.Vsx` | The product of the Visual Studio extension, which hosts Metalama and PostSharp in one process |

A type belongs in one of the packages that depend only on the abstractions when a component that starts no service
needs it, such as a build task loaded by MSBuild. Those packages must not gain a dependency on
`SharpCrafters.Backstage`. A type that needs the logging implementation, the configuration or the standard directories
belongs in `SharpCrafters.Backstage`.

## Hosting several products in one process

The Visual Studio extension serves both Metalama and PostSharp. It creates one service provider from the product of
the `SharpCrafters.Backstage.Vsx` package:

```csharp
var serviceProvider = BackstageServiceFactory.CreateServiceProvider( new BackstageInitializationOptions( applicationInfo, VsxProduct.Instance ) { ... } );
```

For its usage and exception reports, the extension is a product of its own: they are sent under the name
`Visual Studio Tools for PostSharp and Metalama`, and its analytics events go to the analytics site of the extension.
It keeps the configuration of Metalama, so its consents, notifications and device identifier are those of Metalama, and
its license product catalog is the catalog of Metalama. The service provider is the outer provider: it holds the services of Metalama, the primary member, and runs
the only logger, usage and exception telemetry, notifications, welcome page and RSS client of the process.

PostSharp gets an inner service provider, which is created when it is first needed and disposed with the outer one. An
inner provider has no user interface, notifications, welcome page, RSS client, crash dump service or tools extractor,
and collects no usage or exception telemetry. It uses the instances of the outer provider for the services that do not
depend on the product, which are the service types marked with `ProductNeutralServiceAttribute`, such as the logger
factory, the clock, the HTTP client factory, the event dispatcher, the file system and the registry. It keeps its own
configuration store, so Metalama keeps its files and PostSharp keeps its registry keys, and the build of each product
reads what the extension writes. An inner provider has its own unattended process detector, because each product reads
its own `<PREFIX>FORCE_ATTENDED` variable.

Each product audits its own licenses. The audit report goes to the upload queue of the product, and the analytics event
to the analytics site of the product. The outer provider audits the licenses of Metalama with the analytics site of
Metalama. The inner provider audits the licenses of PostSharp, and the extension uploads the queue of PostSharp in its
own process, once a day like any upload, because the worker of PostSharp may not have been extracted on the machine.

The following services of the outer provider answer for all products:

| Service | Behaviour |
|---|---|
| `ILicenseRegistrationService` | Lists the licenses and the editions of every product. A license key is stored by the product family to which its product is native (`ILicenseProductCatalog.IsNativeProduct`), so a PostSharp Framework or PostSharp Ultimate key, which Metalama also accepts, is stored by PostSharp. A license server is stored by Metalama. `RemoveLicenses` removes the licenses of every product |
| `ILicenseConsumptionService` | A requirement is satisfied when the licenses of any product satisfy it. When none does, one message and one notification name the licenses of every product. A license key of the project is given only to the product that owns it |
| `ITelemetryConfigurationService` | Reports the maximal effective consent over all products, unless Metalama is opted out at the process level. Writes a consent to every product. A compare-exchange of a consent is made for each product against its own consent |
| `IIdeExtensionStatusService` | The status is product-neutral: every product reads it from `HKEY_CURRENT_USER\Software\SharpCrafters\IdeExtensions`. The composite service also writes it to the configuration store of every product, where earlier versions of the products read it |

The composite services update the products one after the other. A configuration file must not be updated inside the
transformation of another one, so a product is never updated from inside the update of another product.

## Documentation

| Document | Subject |
|---|---|
| [`docs/license-server.md`](docs/license-server.md) | The license server protocol, where a lease is stored, and when a seat is taken |
| [`docs/telemetry.md`](docs/telemetry.md) | The telemetry channels, consent, and the path of an exception report |
| [`docs/testing.md`](docs/testing.md) | The testing doctrine: what is tested where, how a test is documented, and where a fake lives |

## Building

The build entry point is `Build.ps1`, the front end of [PostSharp.Engineering](https://github.com/postsharp-ops/PostSharp.Engineering).

```powershell
.\Build.ps1 build
```

Run `.\Build.ps1 --help` for the full command set.

## Branches

| Branch | Purpose |
|---|---|
| `develop/2027.0` | Continuous development and integration |
| `release/2027.0` | Updated by deployment only |

Work happens on `topic/2027.0/XXXX-description` branches that are merged into `develop/2027.0`.

## History

The code of this repository was extracted from the `Metalama.Backstage` folder of the
[Metalama](https://github.com/metalama/Metalama) repository with its full history, using `git filter-repo`.
The branch `mirror/metalama-2026.1` is the result of that extraction from the `develop/2026.1` branch of Metalama.
The branch `develop/2026.1` continues it: changes made to the same folder in Metalama 2026.1 are applied to it as a
diff, and reach `develop/2027.0` of this repository by merging `develop/2026.1`. See
[docs/upstream-merge.md](docs/upstream-merge.md).
