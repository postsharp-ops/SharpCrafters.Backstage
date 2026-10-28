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
| `Metalama.Backstage`, `PostSharp.Backstage` | What each product family adds: its profile, its licence catalogue, its web links and its telemetry endpoints |

A type belongs in one of the packages that depend only on the abstractions when a component that starts no service
needs it, such as a build task loaded by MSBuild. Those packages must not gain a dependency on
`SharpCrafters.Backstage`. A type that needs the logging implementation, the configuration or the standard directories
belongs in `SharpCrafters.Backstage`.

## Hosting several products in one process

A process that serves both Metalama and PostSharp, such as the Visual Studio extension, creates one service provider
from a composite product:

```csharp
var product = BackstageProduct.CreateComposite( MetalamaProduct.Instance, PostSharpProduct.Instance );
var serviceProvider = BackstageServiceFactory.CreateServiceProvider( new BackstageInitializationOptions( applicationInfo, product ) { ... } );
```

The first argument is the primary product. The service provider is the service provider of the primary product: it
has its profile, its web links, its telemetry endpoints and its configuration store, and it runs the only logger,
telemetry pipeline, notifications, welcome page and RSS client of the process. Its license product catalog is the
union of the catalogs of all products.

Each other product gets an inner service provider, which is created when it is first needed and disposed with the
outer one. An inner provider has no user interface, notifications, welcome page, RSS client, crash dump service or
tools extractor. It uses the instances of the outer provider for the services that do not depend on the product, which
are the service types marked with `ProductNeutralServiceAttribute`, such as the logger factory, the clock, the HTTP
client factory, the event dispatcher, the file system and the registry. It keeps its own configuration store, so
Metalama keeps its files and PostSharp keeps its registry keys, and the build of each product reads what the extension
writes. An inner provider has its own unattended process detector, because each product reads its own
`<PREFIX>FORCE_ATTENDED` variable. Its telemetry stays in its upload queue: it uploads nothing and sends no analytics,
and the processes of that product upload the queue.

The following services of the outer provider answer for all products:

| Service | Behaviour |
|---|---|
| `ILicenseRegistrationService` | Lists the licenses and the editions of every product. A license key is stored by the product family to which its product is native (`ILicenseProductCatalog.IsNativeProduct`), so a PostSharp Framework or PostSharp Ultimate key, which Metalama also accepts, is stored by PostSharp. A license server is stored by the primary product. `RemoveLicenses` removes the licenses of every product |
| `ILicenseConsumptionService` | A requirement is satisfied when the licenses of any product satisfy it. When none does, one message and one notification name the licenses of every product. A license key of the project is given only to the product that owns it |
| `ITelemetryConfigurationService` | Reports the maximal effective consent over all products, unless the primary product is opted out at the process level. Writes a consent to every product. A compare-exchange of a consent is made for each product against its own consent |
| `IIdeExtensionStatusService` | Reads the status of the primary product, and writes it to every product |

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
