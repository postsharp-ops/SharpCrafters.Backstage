# Platform tests

The platform tests run the platform-specific code of the Backstage packages on the platform itself: Linux and Windows in
a container, and macOS on the agent. The unit tests in `src/tests` replace the operating system with fakes and run on
Windows only, so they cannot tell whether `/proc`, `ps`, `lsof`, `ioreg`, a named mutex or a file mode behaves as the
code expects.

## What belongs here

A test belongs here only when all of the following conditions hold:

- The code under test has a branch per operating system, architecture or container, or it calls a facility of the
  operating system.
- The unit tests cannot reach that code, because they replace the facility with a fake.
- The test asserts what depends on the platform, and nothing else. It does not test the behaviour of .NET itself.

Everything else belongs to the unit tests: timeouts, retry policies, parsing, configuration and licensing rules. When a
platform test needs a parsing function, the function is extracted and tested by the unit tests as well, and the platform
test checks only that the real platform produces what the function parses. Windows code already runs in the unit tests
on a Windows host. The Windows container runs only the behaviour that differs inside a container.

Keeping this rule keeps the two test suites from testing the same thing twice.

## How the tests run

The tests are a Microsoft.Testing.Platform application of the main solution, like the unit tests, and they reference the
product through `ProjectReference`. `Build.ps1 build` packs them into a test archive,
`artifacts/tests/SharpCrafters.Backstage.PlatformTests.net10.0.zip`, and the test agents run the archive with
`eng/RunTests.ps1`. See `doc/testing-platform.md` in PostSharp.Engineering.

`Build.ps1 test` runs them as well, on the build host, because `dotnet test` runs every test application of the
solution. There, the tests of the other platforms are skipped, and so are the tests that need a declared kind of host.

The agents are declared in `eng/src/Program.cs`. `Build.ps1 generate-scripts` creates one TeamCity build configuration
per agent, in the `Platform Tests` sub-project:

| Platform | Where the tests run |
|---|---|
| `win-x64` | A container of the image of the product build, `eng/docker/build.Dockerfile`. |
| `linux-x64`, `linux-arm64` | A container of `eng/docker/linux-x64-build.Dockerfile` or `eng/docker/linux-arm64-build.Dockerfile`, which carry PowerShell and the .NET SDK. |
| `osx-arm64` | The macOS agent itself, because no container engine provides a macOS container. |

## Layout

| Path | Content |
|---|---|
| `SharpCrafters.Backstage.PlatformTests/` | The tests. One namespace per suite. |
| `SharpCrafters.Backstage.PlatformTests/TestPrepare.ps1` | Runs on the agent before the tests. It checks that the agent declares the kind of host, and it installs the apphost of the helper for the platform of the agent. |
| `SharpCrafters.Backstage.PlatformTestHelper/` | The second process of the cross-process tests. |

The tests run the helper under its own name, which only an apphost gives. The build writes the apphost for Windows only,
so the test project also builds the helper for `linux-x64`, `linux-arm64` and `osx-arm64` and packs their apphosts
under `apphosts/<runtime>/`. `TestPrepare.ps1` copies the apphost of the agent beside the helper assembly, makes it
executable and, on macOS, signs it.

A test declares where it runs with `[PlatformFact( TestPlatforms.Unix )]` or
`[PlatformFact( TestPlatforms.All, TestHosts.Container )]`. The kind of host comes from the variable
`BACKSTAGE_PLATFORM_TEST_HOST`, which the build configuration of each agent sets, because detecting it is part of the
code under test. The attributes are in `SharpCrafters.Backstage.Testing`, and the unit tests use them as well, for
instance `[PlatformFact( TestPlatforms.Windows )]` for a test of the registry.

## Running the tests

Build the product first with `Build.ps1 build`, which writes the test archives. Then run the archive where the tests
must run, and declare the kind of host:

```powershell
$env:BACKSTAGE_PLATFORM_TEST_HOST = 'container'   # or 'host'
pwsh ./eng/RunTests.ps1 -Name SharpCrafters.Backstage.PlatformTests.net10.0
```

The process management suite runs the `shutdown` command, which stops every process that the product recognizes on the
machine, including the compiler server and MSBuild nodes. Run it in a container, or on an agent that runs one build at a
time. Its test of the build servers runs in a container only.
