The `SharpCrafters.Backstage.Worker` library is not packed. It is consumed through project references by the worker executables of the products in this repository.

It contains the worker application of Backstage as a library: the local setup web server with its pages (license key, privacy consents, news, exception review) and the telemetry upload command. Each product hosts it in a small executable that registers its own Backstage services; the executable of Metalama is `Metalama.Backstage.Worker`.
