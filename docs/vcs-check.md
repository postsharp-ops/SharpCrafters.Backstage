# The version control check

> Verified against the implementation on 2026-09-17.

`IVcsStatusService` answers one question: are any of these files modified in version control? A product uses
the answer to decide whether a build deserves the full licensing treatment. A source tree that nobody has
touched is a source tree that nobody is developing, and compiling it should not cost a seat.

The check is opt-in. Metalama enables it with the `MetalamaVcsCheckEnabled` MSBuild property; PostSharp has
its own `VcsCheckEnabled` project property and its own, older implementation, which will be refactored onto
this service. Both default to off.

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

**Every ambiguity is resolved in the customer's favour.** The check exists to avoid charging a seat for work
nobody did. A rule that counted generated files as modifications would turn a clean tree into a modified one
on the first build and make the feature useless.

**Every failure is resolved against the customer.** If git is not installed, exits with a non-zero code, times
out, throws, or if no file belongs to any repository, the service reports the files as modified and licensing
is enforced. `IsAnyFileModifiedAsync` therefore returns `true` both for "modified" and for "cannot tell", and
its documentation says so in its first sentence.

## Accepted limits

These are known, deliberate, and written down here so that they are not rediscovered as bugs.

**1. Staged modifications count, which PostSharp's implementation does not do.** PostSharp counts only the
unstaged `" M"`, so staging an edit waives the check. That is a hole, and this service closes it. A product
migrating onto this service should expect the verdict to change for a user who works with a dirty index.

**2. Files outside a repository are ignored, so a token repository is enough to pass the check.** Initialise
a repository in an empty directory, commit one file, add it to the project, and every other source file of
that project is ignored because it belongs to no repository. This is PostSharp's rule, kept for
compatibility. The service logs the count of ignored files at the `Info` level, so a support case can see
what happened.

**3. A build that the check exempts reports nothing to the licence audit.** See the rule below.

**4. An unattended build is a policy decision of the caller, not of this service.** A CI agent clones fresh,
so nothing is ever modified and the check would exempt every CI build. Seats are safe there — an unattended
process never takes a lease, as [license-server.md](license-server.md) records — but *enforcement* would
disappear on the machines that compile the most. Metalama therefore suppresses the check on an unattended
process, and offers `MetalamaVcsCheckOnUnattendedBuild` for the cases that genuinely want it, among them its
own container-based end-to-end tests.

Whether a container counts as unattended is less obvious than it looks. `ProcessUtilities` recognises one
by looking for `docker` in `/proc/1/cgroup`, which says nothing under cgroup v2 — what Docker on WSL2 uses —
or for `container=docker` or `DOTNET_RUNNING_IN_CONTAINER=true` in the environment of PID 1, which the
official .NET images set and an image built by unpacking the SDK onto a plain distribution does not. So a
hand-rolled container can be taken for an interactive session. The container tests set the variable in their
own Dockerfile rather than relying on the detection.

## The seat rule

> **Rule.** A build skipped by the version control check consumes no licence and therefore **reports nothing
> to the audit**. The audit counts people who *modify* code, not people who compile it. The under-count is
> deliberate and must not be repaired by reporting a use that no licence satisfied — a `ReportUse` without a
> `TryConsume` would attribute a build to a licence that was never resolved.

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

What is cached is **git's answer about the repository, not the verdict about a project**. That is what lets
one record serve every project: each derives its own verdict by intersecting its own file list with the
cached list of modified paths. A cache holding the queried file list instead — which is what PostSharp does —
is invalidated by the very next project of the same repository, so nothing is ever reused and every writer
overwrites the previous one.

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

Concurrent queries of the same repository within one process share a single command. They are **not** shared
between processes: doing so would mean holding a machine-wide lock across the command, and `INamedLock` has
thread affinity and cannot be held across an `await`; running the command synchronously under such a lock
instead would give up the cancellation that callers require. On a cold cache the cost is therefore one
command per build node, and none at all once L2 is warm.

Every failure of the cache is a miss and a trace record. A miss costs one command; an exception would fail
the build, and a wrong hit would waive the check. The file is written atomically, because a truncated file
still parses as a valid record — one in which a modified file is simply missing, which is the dangerous
direction.
