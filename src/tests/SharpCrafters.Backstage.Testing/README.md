The `SharpCrafters.Backstage.Testing` package is not published and is not meant to be referenced in user projects.

It contains the test helpers for the Backstage services: a test base class with an in-memory file system, environment, clock and configuration, and a provider of test license keys. The test projects of the Metalama and PostSharp products use it.

A helper belongs here because a test project of another repository needs it, not because two test projects of this one happen to: everything in this package is a commitment to the repositories that consume it. A fake used by a single test project lives beside the tests that use it. See [`docs/testing.md`](../../../docs/testing.md).
