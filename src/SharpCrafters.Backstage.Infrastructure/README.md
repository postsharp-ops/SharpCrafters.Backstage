# SharpCrafters.Backstage.Infrastructure

The part of the Backstage infrastructure that a consumer can take without taking the Backstage services: named
locks, the detection of the processes holding a file, the runtime information, the detection of an unattended
process and of a continuous integration server, the process kinds, and the contracts of the services those need.

It exists for the build tools of the Metalama and PostSharp products. Such a tool is loaded by MSBuild into a
process it does not own, so it is merged into a single assembly and can take no dependency. This package therefore
has none beyond `SharpCrafters.Common`, which is itself dependency-free: no serializer, no registry, no packaging
and no hashing library.

Everything above that — the logging, the configuration, the standard directories, the file system, the telemetry,
the licensing and the user interface — is in `SharpCrafters.Backstage`, which references this package. A consumer
that can take that package needs nothing from this one directly.

This package is not published to a public feed.
