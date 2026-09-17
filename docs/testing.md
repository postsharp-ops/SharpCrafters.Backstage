# Testing

This document is the doctrine the tests of this repository follow. It exists because the two mistakes it guards against are the ones that look like diligence: a test suite that grows by testing the same rule at every layer, and a test suite nobody can read when it turns red.

## Test the API, smoke-test the shim

**A rule is tested where it lives.** Licensing rules are tested against `ILicenseRegistrationService`, `ILicenseConsumptionService` and the services beneath them. Configuration rules are tested against `IConfigurationManager`. When such a test fails it names the rule that broke.

**Commands are shims.** Everything under `SharpCrafters.Backstage.Commands` parses arguments, calls one service and prints the answer. What a command owns is that wiring and that output, and nothing else. So each command gets **one smoke test on the path a user takes when everything works**, and its failures, edge cases and boundaries are tested against the service it calls.

> **Rule.** Before adding a command test, ask what would have to break for it to fail that would not also break a test of the service. If the answer is "nothing", the test belongs to the service.

The cost of ignoring this is not the running time. It is that every rule tested twice is a rule that has to be changed twice, and that a failure in the command tests tells you a command is broken when what broke is a service — so the next person reads the wrong code first.

The same reasoning applies anywhere one layer merely forwards to another: the user interface pages of `SharpCrafters.Backstage.Worker`, the product-specific wrappers in `Metalama.Backstage`.

## Say what a test protects, in the terms of the customer

Every test carries an XML documentation summary that says **which promise it keeps and what the customer would lose if it stopped passing** — not what method it calls, which the name already says and the body already shows.

```csharp
/// <summary>
/// Tests the spelling of the machine hash. A license server tells machines apart by the text it receives, so the
/// same machine spelled in two cases would be accounted as two, and a team of ten would exhaust twenty seats.
/// </summary>
[Fact]
public async Task MachineHashIsLowerCaseHexadecimal()
```

> **Rule.** The summary answers the question a maintainer has when a test turns red: *is this behaviour still wanted?* A summary that restates the assertion cannot answer it, and the test then gets edited until it passes.

Where the rule needs a technical explanation as well — the exact bytes of a wire format, a difference between two target frameworks, the defect a guard exists for — that goes in `<remarks>` beside the summary, not instead of it.

## Where a fake lives

| Kind | Home |
|---|---|
| Needed by the test projects of Metalama and PostSharp | `SharpCrafters.Backstage.Testing`, which is packed for them |
| Needed by more than one project of this repository | `SharpCrafters.Backstage.Testing` |
| Needed by one test project | That test project, beside the tests that use it |

`SharpCrafters.Backstage.Testing` is a package. Everything in it is a commitment to the repositories that consume it, so a fake goes there because something outside this repository needs it, not because two test projects here happen to.

> **Rule.** "Two of our own test projects need it" is a reason to look for a smaller fake, not a reason to publish the bigger one. `LicenseServerSimulator` was in the package for exactly that reason; once the command tests were reduced to smoke tests they needed a canned response of a dozen lines, and the simulator moved beside the tests that really use it.

## A fake is not the product

A fake that shares code with the thing it tests cannot disagree with it. `LicenseServerSimulator` writes the wire format of a license server **by hand** and shares no serializer with the product, because a symmetric mistake in a shared one would be invisible to every test.

> **Rule.** When a fake stands for something outside this repository — a deployed server, a protocol, a file another product writes — it reproduces that thing from its specification, and at least one test pins a literal captured from the real thing.

## Determinism

No test waits for the wall clock, opens a socket or binds a port.

- **Time** comes from `IDateTimeProvider`. `TestDateTimeProvider.AddTime` crosses an expiry, a retry period or a renewal window instantly. A `Task.Delay` in a test is a defect, whatever it is waiting for.
- **HTTP** goes through `IHttpClientFactory`. `TestHttpClientFactory.InsertHook` answers a request in process, so a test that exercises a network failure does so without one.
- **The file system** goes through `System.IO.Abstractions`; `TestsBase` gives each test its own.
- **The configuration** is in memory, per test.

Anything that cannot be exercised this way — a TLS handshake, Windows integrated authentication, a real proxy — is run by hand, from a utility under `src/utilities`, and the document of the subsystem says so plainly rather than implying coverage that does not exist.

## Running them

```powershell
.\Build.ps1 test
```

`SharpCrafters.Backstage.Tests` runs on both `net10.0` and `net472`, because the two frameworks disagree about things the product depends on — how an HTTP timeout surfaces, for one.
