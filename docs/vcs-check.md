# The version control check

> Verified against the implementation on 2026-09-18.

`IVcsStatusService` answers one question: are any of these files modified in version control? A product uses
the answer to decide whether a build deserves the full licensing treatment. A source tree that nobody has
touched is a source tree that nobody is developing, and compiling it should not cost a seat.

The check is opt-in and off by default. Metalama selects it with the `MetalamaVcsCheck` MSBuild property,
which takes one of three modes:

| Mode | An unmodified tree | A modified file |
|---|---|---|
| `Disabled` (default) | licensing runs | licensing runs |
| `FailOnChange` | licensing waived | **the build fails** |
| `AcquireLicenseOnChange` | licensing waived | licensing runs |

The two modes that run the check agree about the unmodified tree, which is the case the feature exists for.
They differ only in what a modification means, and that is a decision of the repository rather than of this
service: `AcquireLicenseOnChange` treats it as a use to be licensed, while `FailOnChange` treats it as a
mistake to be reported, for sources that are meant to be compiled as they are.

`IVcsStatusService` itself knows none of this. It answers whether the files are modified; the mode is applied
by the product, in Metalama's case by the `VerifyMetalamaLicense` task.

An unrecognized value fails the build rather than falling back to a mode. It is a typo in a build script, and
either fallback would be wrong for somebody.

This document is the doctrine. The XML documentation of `IVcsStatusService` states the rule; the reasoning
and the accepted limits are here.

## The rule

A file counts as **modified** when, and only when, git reports a **content modification of a tracked file** —
a `git status --porcelain=v1` entry in which either column is `M`. That covers the unstaged modification
(`" M"`), the staged one (`"M "`) and both at once (`"MM"`).

Everything else does **not** count:

| Not counted | Why |
|---|---|
| Untracked files (`"??"`) | A build writes `*.g.cs` into `obj`, a restore writes sources into the package folder. None of it is the user's work, and none of it is tracked. |
| Additions (`"A "`) | A file staged for addition is usually a generated file that someone added by accident, or a new file that has not been compiled before. |
| Deletions (`"D "`, `" D"`) | A file that is gone is not a file that the user has modified, and it cannot be compiled either. |
| Renames and copies (`"R "`, `"C "`) | The content is unchanged; only the path moved. |
| Ignored files | By definition, not the user's tracked work. |
| Files in no git repository | See the accepted limits below. |

The rule follows one distinction: whether the customer controls the condition and can act on it.

A global failure is one the customer controls. Git is not installed, or the command exits with a non-zero
code or times out. The service reports the files as modified and licensing is enforced, which is what makes
the condition visible and gets it fixed.

A project that belongs to no repository at all is not a failure of that kind and is not reported as a
verdict either. The question has no answer, so `IsAnyFileModifiedAsync` throws `InvalidOperationException`
and the caller decides. Metalama lets it fail the build, because a project configured to use the check and
not kept in version control is a contradiction the user has to resolve.

A file-specific inconsistency is one the customer does not control. A generated file appears in `obj`, a
package ships a source file, a tool writes something the repository does not track. These are tolerated: a
rule that counted them would turn a clean tree into a modified one on the first build and make the feature
useless, and the customer has no way of stopping it.

`IsAnyFileModifiedAsync` therefore returns `true` both for "modified" and for "cannot tell", and its
documentation says so in its first sentence.

## Accepted limits

These are known, deliberate, and written down here so that they are not rediscovered as bugs.

**1. A token repository is enough to pass the check.** Files outside a repository are ignored, so a
repository initialised in an empty directory, with one committed file added to the project, leaves every
other source file of that project ignored. The service logs the count of ignored files at the `Info` level,
so a support case can see what happened.

**2. A build of unmodified files reports nothing to the licence audit.** See the rule below.

**3. The check does not run on an unattended build.** Under `AcquireLicenseOnChange` this is a performance
decision and nothing more: an unattended process never takes a lease, as
[license-server.md](license-server.md) records, so running the command there would cost time and change no
outcome. A caller that wants it anyway asks for it: Metalama offers `MetalamaVcsCheckOnUnattendedBuild`,
which its container tests use because they build in a container and still mean to exercise the check.

Under `FailOnChange` the same exclusion does change the outcome, because a modified file would have failed
the build. The exclusion is applied to both modes all the same, so that the mode does not silently decide
whether a rule runs on the build server. The consequence is that `FailOnChange` enforces nothing on an
unattended build unless `MetalamaVcsCheckOnUnattendedBuild` is set with it, which is a sharp edge for the
continuous integration build, where the rule is likeliest to matter. That is a deliberate choice, recorded
here rather than papered over: a repository that wants the rule enforced on its build server sets both
properties.

## The seat rule

> **Rule.** A build of unmodified files consumes no licence and therefore reports nothing to the audit. The
> audit counts the people who modify code, not the people who compile it. The under-count is deliberate and
> must not be repaired by reporting a use that no licence satisfied: a `ReportUse` without a `TryConsume`
> attributes a build to a licence that was never resolved.

[license-server.md](license-server.md) is where the rest of the seat accounting lives, and its warning
applies here too: a mistake in seat accounting is invisible. Nothing fails; the organization simply runs out
of seats, or stops being counted at all.

## How it works

For each file, the service walks up from its directory until it finds a `.git` entry — a directory in a
normal clone, a file in a submodule or a linked working tree — and groups the files by repository root. It
then runs, in each root:

```
git --no-optional-locks status --porcelain=v1 -z --untracked-files=no --ignore-submodules=all
```

Each option earns its place:

- `--no-optional-locks`, before the subcommand because it is an option of git itself, stops `git status` from
  refreshing and rewriting the index. Without it, parallel build nodes write the same file, and the
  modification time of the index — which the cache uses as a staleness signal — becomes noise this service
  generated itself.
- `-z` is a correctness requirement, not an optimization. Without it git C-quotes paths under
  `core.quotepath`, so a non-ASCII path arrives escaped in one configuration and as raw bytes in another. The
  price is that renames and copies emit two records instead of one; the parser consumes the second even
  though it ignores those statuses, because otherwise it would read a path as a status code and misread
  everything after it.
- `--untracked-files=no` skips the walk of the whole working tree, which is the expensive part on a large
  repository, and untracked files do not count anyway.
- `--ignore-submodules=all` removes entries the service would otherwise parse and discard.

The command is `git`, found on the search path. The `METALAMA_GIT_PATH` environment variable names another
one, for a machine on which git is installed but is not on the search path, or which carries several
installations.

The standard output is decoded as UTF-8 explicitly, because the default is the code page of the console. The
`GIT_DIR`, `GIT_WORK_TREE`, `GIT_INDEX_FILE`, `GIT_COMMON_DIR`, `GIT_OBJECT_DIRECTORY` and
`GIT_CEILING_DIRECTORIES` variables are **removed** from the environment of the child process — removed, not
set to an empty string, because git distinguishes the two and an empty `GIT_DIR` makes it refuse to run. A
build started from a git hook inherits those variables and would otherwise be told about a temporary index
rather than the repository.

Paths are compared without regard to case on every platform. The choice follows from which mistake is
affordable: an over-match reports a file as modified and enforces licensing, which costs us; an under-match
misses a genuine modification and waives enforcement, which costs the customer.

The repository root is found by walking for `.git` rather than by asking `git rev-parse --show-toplevel`,
which would be the obvious alternative. `rev-parse` reports the *resolved physical* path — symbolic links
followed, substituted drives and junctions expanded — while the caller passes the paths its build system gave
it. Combining git's relative paths onto a resolved root yields paths that never compare equal to the
caller's, and a file that does not compare equal is a file that is not found among the modified ones. The
walk keeps both sides of the comparison in one spelling.

## The cache

Running `git status` once per project would be one command per project in a solution; running it once per
repository is one command for all of them. The cache is what makes the difference, and it has two layers,
both keyed by repository root and both governed by the same validity rule:

| | Layer | Scope | What it saves |
|---|---|---|---|
| **L1** | in-memory map in the service instance | one build node | every project after the first that the node compiles |
| **L2** | one file per root under the Backstage temp directory | every process on the machine | the other build nodes, and subsequent builds |

What is cached is git's answer about the repository, not the verdict about a project. That is what lets one
record serve every project: each derives its own verdict by intersecting its own file list with the cached
list of modified paths. A cache holding the queried file list instead would be invalidated by the very next
project of the same repository, so nothing would ever be reused and every writer would overwrite the
previous one.

Each record carries a timestamp taken **before** the command starts. A record stamped when it was *written*
would cover the interval during which the command was running, and a file modified in that interval would
read as unmodified for as long as the record lived.

A record is still valid when all of the following hold:

- `.git/index` and `.git/HEAD` were last written before the timestamp. Two extra file stats, and they catch a
  branch switch, a reset, a stash, a staging operation and a rebase — everything that changes the answer
  without changing the modification time of any source file.
- No queried file was written at or after the timestamp, less a two-second margin. The margin absorbs the
  modification-time granularity of FAT, exFAT and SMB volumes.

The validity rule is applied **only to a record read from a cache**, never to one the command has just
produced. A fresh record is authoritative by construction, and the margin would otherwise reject it whenever
the build had just written one of the files it compiles — which is exactly what a build does to the sources
it generates.

Concurrent queries of the same repository within one process share a single command. They are not shared
between processes: doing so would mean holding a machine-wide lock across the command, and `INamedLock` has
thread affinity and cannot be held across an `await`; running the command synchronously under such a lock
instead would give up the cancellation that callers require. On a cold cache the cost is therefore one
command per build node, and none at all once the file layer is warm.

The interleavings that matter are driven in the tests through `ITestSynchronizationProvider` rather than
waited for, so that the sharing is established rather than assumed. The synchronization points are named
after the repository, which is why two instances querying one repository meet at the same point.

Every failure of the cache is a miss and a trace record. A miss costs one command; an exception would fail
the build, and a wrong hit would waive the check. The file is written atomically, because a truncated file
still parses as a valid record — one in which a modified file is simply missing, which is the dangerous
direction.
