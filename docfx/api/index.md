# API reference

The public types of `Savepoint.Core`, generated from the doc comments in the code. The app talks to the core only through the interfaces in `Savepoint.Core.Api` (`IGamesApi`, `ISettingsApi` and the rest); the other namespaces hold the records and errors those interfaces pass around.

Each interface's comments state its contract: what it returns, what it refuses, and which errors it reports. How the layers fit together is in the [architecture guide](../../docs/architecture.md).
