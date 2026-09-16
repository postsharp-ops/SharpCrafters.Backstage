The `SharpCrafters.Common` package is not meant to be referenced in user projects.

It is the library that the production assemblies of every layer of the Metalama and PostSharp products may reference, so it has no dependency and stays small. It defines the test-only hooks that production code exposes, namely synchronization points and fault injection points. These services are never registered in production, so a hook costs a null check.
