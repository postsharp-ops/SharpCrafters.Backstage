# License server

This document describes how a license server works inside `SharpCrafters.Backstage`: what the wire protocol is, where a lease is stored, and — the part that is easy to get wrong — **when a seat is taken from the customer's pool**. It exists because a mistake in seat accounting is invisible: nothing fails, the organization simply runs out of seats, and the person who reports it is not the person whose build took them.

A license server is a PostSharp component (source at [postsharp/PostSharp.LicenseServer](https://github.com/postsharp/PostSharp.LicenseServer)) that a customer deploys on their own network. A license string may be the URL of such a server instead of a license key; the product then leases a key from it for a few days. The wire protocol is the one deployed servers already speak and **must not be changed**.

## When a seat is taken

This is the question to answer before changing anything in this subsystem.

| Operation | Contacts the server | Takes a seat |
|---|---|---|
| `ILicenseSource.GetLicenses` | no | no |
| any of them, in an **unattended** process | **never** | **never** |
| `LicenseFactory.TryCreate` | no | no |
| `ILicenseConsumptionService.CreateConsumerAsync` | **yes**, unless a stored lease is valid and not due for renewal | **yes**, on the first acquisition for this machine |
| `ILicenseConsumer.TryConsume` | no | no |
| `ILicense.ReportUse` (the audit) | no | no |
| `ILicenseRegistrationService.RegisterLicenseAsync` | **yes**, always | **yes** |
| `ILicenseRegistrationService.ResolveLicenseAsync` | **yes**, always, for a URL | **yes** |
| `ILicenseRegistrationService.AcquireLeaseAsync` (`license acquire-lease`) | only when the stored lease is due, or **always** with `--force` | **yes**, when it downloads; requires an interactive session |
| `ILicenseRegistrationService.RegisteredLicenses` (`license list`) | no | no |
| `ILicenseRegistrationService.RemoveLicenses` (`license unregister`) | no | releases nothing before the lease ends |

**Creating a consumer is where a lease is acquired.** Every licence of every source is resolved by the time `CreateConsumerAsync` returns, so that `TryConsume` — which a compilation calls once per requirement, on its critical path — neither waits nor allocates. `TryConsume` cannot take a seat, because by then there is nothing left to acquire.

> **Rule.** `ILicenseConsumer.TryConsume` is synchronous and stays synchronous. Anything that would make it wait for a network belongs in `CreateConsumerAsync` instead.

> **Rule.** Enumerating an `ILicenseSource` costs no I/O either. It turns license strings into `ILicense` objects and does nothing else, which is why `GetLicenses` is a plain `IEnumerable<ILicense>`. The server is contacted one step later, when the consumption service resolves the licence.

A *seat* is held by a **user and machine for the life of the lease**, not by a build. With the server defaults — a three-day lease, renewed after two — one machine contacts the server about once every two days, whatever the number of builds in between. Acquiring is therefore better read as "registering this machine with the server for the next three days" than as "paying for this build".

`LicenseServerClient.GetLeaseAsync` downloads only when there is no stored lease, when the stored one has expired, or when it is past its renew time. Everything else is served from `licenseServer.json`.

> **Rule.** **An unattended process never leases.** It is licensed by the unattended license, which costs nothing, so taking a seat would hold one that the people who need it cannot get — and a build server builds far more often than a developer, so it would hold several. A license source skips a registered license server when the process is unattended, whether the URL comes from the user profile or from the build itself, and `license acquire-lease` requires an interactive session for the same reason. The skip is silent: what the user configured is right, the process it does not apply to is not the one whose user could act on a message, and saying it would mean a warning on every build of a continuous integration server, for ever.

> **Rule.** There is no way to ask a license server what it *would* lease without leasing it, so nothing here may be named as though there were. `license acquire-lease` is called that because that is what it does: it takes the same path a build takes, takes a seat and stores the lease, which is also what makes it a faithful diagnostic — what the user sees is what their next build will see. `--force` renews a lease that is not yet due, because a machine that already holds one would otherwise contact nothing and the command would report nothing about the server.

### What this costs, and why it is accepted

A licence key and a license server cannot both be registered in the user profile: `LicensingConfiguration.SetLicense` removes one when the other is registered. The two can only coexist when a higher-priority source supplies the key, which now means one case: a project key from MSBuild, on the machine of a developer. The unattended licence, which was the other case, no longer produces it, because an unattended process does not reach a license server at all.

In that configuration **the server is contacted and a seat is held although the key licensed the build**. This is deliberate. The alternative — skip the server when a higher-priority source already yielded a valid licence — cannot be decided at the moment it would have to be: the requirements of the compilation are not known while the consumer is being built, so a key that is valid but not eligible for a particular requirement would leave a build unlicensed that the lease would have licensed. Resolving everything never does that.

The trade is one seat held by a machine that did not need it, against a build that fails although a licence was available. It is also symmetric with a licence key, which is reported to the licence audit whether or not a requirement used it; the ledger of the server is that same accounting in the customer's own hands. And what it costs is now bounded by the machines of the developers, which is the population the seats were bought for: the build servers, which would have cost the most, are out of it.

> **Rule.** A licence is resolved once per `ILicense` instance (`LeasedLicense._resolution`). The consumption service asks a licence that failed for its registration properties in order to name it in the message, so dropping the memo would contact the server twice for one consumer.

## The protocol

### `GET {url}/Lease.ashx`

```
?user={userName}&machine={machineName}-{machineIdHash:x}&version={productVersion}&buildDate={buildDate:o}&product={productCode}
```

The order of the arguments and the shape of `machine` are **load-bearing**. The server strips the `-{hex}` suffix (regex `-[0-9a-fA-F]+$`) before matching a machine against its `BuildServers` setting, so a client that sent a bare machine name would break build-server detection. `machineIdHash` is `HashUtilities.ComputeStringHash64( IMachineIdProvider.MachineId )`, which is byte-for-byte what PostSharp sent, so existing seat accounting carries over.

`product` is ours: `Lease.ashx` reads it and selects the licence pool, but the PostSharp client never sent one. It lets a single server hold a Metalama pool and a PostSharp pool side by side; a server that ignores it is unaffected. The value is the enum name, e.g. `MetalamaProfessional`. Absent means "any licence", not "no licence".

An absent `version` is read by the server as `4.9.9`, that is, "a pre-5.0 client", so it must always be sent.

| Status | Meaning |
|---|---|
| 200 | the lease, in the body |
| 400 | a missing or unparsable argument (`user` and `machine` are required) |
| 403 | a denial; **the body is the message** and must reach the user verbatim |
| 503 | `Service overloaded.` — the machine-wide mutex of the server timed out |

> **Rule.** Never call `EnsureSuccessStatusCode` on this response. It discards the body of a 403, which is the only thing that tells the user what to do.

### The response

One line, parsed leniently: split on `;`, split each part at the *first* `:`, lowercase the key, recognize `license`, `starttime`, `endtime` and `renewtime`, ignore everything else.

```
License: 3-ZEQQ…; StartTime: 2026-09-17T08:00:00Z; EndTime: 2026-09-20T08:00:00Z; RenewTime: 2026-09-19T08:00:00Z
```

Only `License` is mandatory. `StartTime` defaults to now, `EndTime` to `StartTime` + 1 day, `RenewTime` to `EndTime`.

### `GET {url}/GetTime.ashx` — the clock protocol

```
{serverUtcNow:xsd};{acceleration:xsd-decimal}
```

The acceleration comes from the server's `TimeAcceleration` app setting, **default 1440**: one real minute is one virtual day. It is honoured only in DEBUG builds of the server. The client side lives in `SharpCrafters.Backstage.Testing.AcceleratedDateTimeProvider` and is used by tests and by the load harness; the product keeps the real clock.

## The seat model of the server

Reproduced faithfully by `LicenseServerSimulator`, because a simulator that disagrees with a real server is worse than none.

- Usage is counted **per user**, as `sum over users of ceil( machinesHeldByUser / MachinesPerUser )`, `MachinesPerUser` defaulting to **2**.
- The fast path that grants a further machine is `machines.Count % MachinesPerUser != 0` — a **modulo, not a comparison**. With the default, any *odd* number of machines already held grants the next one without a capacity check.
- Leases are append-only. A renewal inserts a new row pointing at the old one and preserves the original `StartTime`. Nothing is deleted and there is no reaper: expiry is the predicate `EndTime > now`, evaluated per request.
- Beyond capacity there is a grace period: `GracePercent ?? 30` extra seats for `GraceDays ?? 30` days.
- A machine matching the `BuildServers` setting gets a lease that is **not stored** and consumes no seat.

> **Rule.** Reproduce the modulo, not the intuitive `<`. A simulator that implements the obvious rule passes its own tests and disagrees with every deployed server.

## Storage

| File | Scope | Contents |
|---|---|---|
| `licensing.json` | user | the registered license strings, including a server URL; `allowInsecureLicenseServer` |
| `licenseServer.json` | user | one lease per server URL, keyed by `LicenseServerUrl.GetStoreKey` |

The leases live in a file of their own, not in `licensing.json`, for two reasons. The configuration manager takes one named lock **per file path**, and a lease is written whenever a build renews one, whereas the registered keys are written only when the user registers something; sharing the file would make every build queue behind an occasional command. And the two have different standing: `licensing.json` records what the user decided, a lease is derived state that can be acquired again at any moment.

A registered URL is stored in the group of `LicensingConstants.MinimalLicenseServerVersion` (`2027.0`), so an earlier Metalama reading the same `licensing.json` skips it instead of reporting a parse error the user cannot act upon.

> **Rule.** `LicenseLeaseStore` skips its write when `ConfigurationUpdateScope.IsUpdating`, rather than throwing. A transformation of one configuration file must not update another; a skipped write costs one extra request later, whereas an exception fails a build.

> **Rule.** `RemoveLicenses` performs two **sequential** updates — the licensing configuration, then the lease store — and they must not nest, for the same reason.

## Resolution order

Sources are drained in the order of `LicenseSourcePriority`: `Unattended`, then `Explicit`, then `UserProfile`. `Unattended` being first is what makes "the unattended license wins over the license server" true of the *result*; the rule above is what makes it true of the *cost*, by keeping an unattended process away from the server in the first place. Within the user profile, `LicensingConfiguration.GetRegisteredLicenseStrings` yields the license keys first and the URLs last. A license server is therefore always considered after every license key, which is what decides **which licence satisfies a requirement**, and so which one is audited.

It does not decide whether the server is contacted. See [When a seat is taken](#when-a-seat-is-taken).

## Insecure `http://`

A lease request carries the user name and the machine name, so an `http://` server discloses who works where to anyone on the path. `LicenseServerUrlValidator.TryValidate` returns that as a *warning* beside the error that would refuse the URL, and every caller reports it: `LicenseFactory`, so a build says it once per license string; and `license register` and `license acquire-lease`, the first being the moment the user can still choose a different URL.

It is **never an error**. Refusing the server would fail a build over a deployment the developer did not choose and cannot change. `allowInsecureLicenseServer` in `licensing.json` silences the warning; there is no other setting, and a loopback address is not exempt, because an exemption would also cover a loopback port forwarded to a remote host.

## Rules of PostSharp deliberately not reproduced

Each of these is a defect of the original, not a difference of taste. Reintroducing one is a regression.

1. `TryDownloadLease` returned a lease **and** reported an error when only the registry write had failed, because one `try` covered both. Here a download failure returns a failed result; a cache-write failure is logged only.
2. The expired-lease path wrote to the default registry value inside a loop over *named* values, so an expired version-named lease wiped the version-agnostic one. Structurally impossible now: one lease per URL.
3. `CleanLicenseString` strips everything that is not a letter, digit or `-`, which destroys a URL. `LeasedLicense` must never call it.
4. A failed **renewal** was reported at error severity although the code went on using a still-valid cached lease — a build failure over a licence that was fine. It is a warning here.
5. `Deserialize` caught only `FormatException`; `XmlConvert.ToDateTime` also throws `ArgumentException` and `ArgumentOutOfRangeException`. Catch broadly.

Two further departures, which are corrections rather than omissions:

- The instants of a lease **stay in UTC**. PostSharp converted them to local time and compared them against a local clock, which cancels out only when the client and the server share a time zone.
- A freshly downloaded lease whose `EndTime` is already past is rejected as an invalid response and not cached. Without that guard, clock skew makes a process download, discard and download again forever.

## Testing it

Everything below runs in process, over `TestHttpClientFactory`. There is no socket, no port and no delay: expiry and renewal are driven by `TestDateTimeProvider`.

```powershell
.\Build.ps1 test
```

`LicenseServerSimulator`, in `SharpCrafters.Backstage.Tests/Licensing/LicenseServer` beside the tests that use it, serves `Lease.ashx` and `GetTime.ashx`, reproduces the seat model above, and has twelve fault modes (`LicenseServerFault`). It exposes `Requests`, `AssertContacted`, `AssertNotContacted` and `OccupiedSeatCount`.

> **Rule.** The simulator writes the wire format **by hand** and shares no serializer with the product. The product only ever parses a lease, so sharing would mean adding a `Serialize` nothing calls — and `Deserialize(Serialize(x)) == x` holds for any self-consistent pair, including one that agrees on a format no real server emits. That is exactly the weakness of the fake PostSharp used. One test pins a literal response captured from a real server, and one pins the request byte for byte, including a hard-coded machine hash.

Against a real server, by hand:

```powershell
dotnet run --project src/utilities/LicenseServerLoadSimulator -- http://localhost:44670/
```

It drives a simulated organization on the server's accelerated clock, then prints what happened.

**The harness runs the product.** Each simulated user and machine is a `SimulatedInstallation`: a service provider carrying the real licensing services, with that user's account, machine, configuration and the clock of the server. A build is `CreateConsumerAsync` followed by `TryConsume`, so what reaches the server is what a customer's build would send, and whether a build contacts the server at all is the product's decision rather than the harness's — which is a large part of what a load simulation is measuring. A harness with a lease client of its own measures that client instead, and goes on passing after the product has changed underneath it.

> **Rule.** Nothing in `src/utilities/LicenseServerLoadSimulator` may build a request, parse an answer or decide when to renew. The moment it does, the run stops being evidence about the product.

The configuration of each installation is in memory, so a run leaves the profile of whoever is running it untouched and each simulated user starts from an empty one. The harness also declares itself attended: an unattended process never leases, so a simulation that declared otherwise would send nothing at all.

Operating an accelerated server, all of it undocumented upstream and all of it easy to get wrong:

- Acceleration is `#if DEBUG` on **both** sides. A Release server reports `1440` from `GetTime.ashx` yet returns real time, while the client still divides by 1440 and never sleeps — a request flood against a real-time server, with no error. Build the server in Debug.
- `TimeAcceleration` has two different defaults: `1` in the shipped `web.config` and `1440` in the compiled settings. Set it explicitly.
- The virtual epoch of the server is captured once at static initialization and never resets, and only the current reading is transmitted. Recycle the application pool between runs, or the second run starts days into the future.
- `GetTime.ashx` is not latency-compensated. At 1440× a 50 ms round trip is 72 virtual seconds of skew; the only correction is the re-synchronization the harness performs when a lease arrives already past its `RenewTime`.
- Every 403 sends an e-mail synchronously, with no rate limit. Blank `DeniedRequestEmailTo` before a run that deliberately exhausts seats.

> **Rule.** `Build.ps1 test` does not appear to compile this utility, so nothing tells you when a change to the product has broken it. Build it explicitly after changing anything it touches: `dotnet build src/utilities/LicenseServerLoadSimulator`.

Every rule of this subsystem is tested in `SharpCrafters.Backstage.Tests/Licensing/LicenseServer`, against `ILicenseRegistrationService`, `ILicenseConsumptionService` and `LicenseServerClient`. The commands are shims over those services, so `SharpCrafters.Backstage.Commands.Tests` holds four smoke tests and nothing else, and it does not use the simulator: a smoke test needs a server that answers, not one that keeps seats. See [`docs/testing.md`](testing.md).
