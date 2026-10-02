# Merging changes from Metalama 2026.1

The code of this repository was extracted from the `Metalama.Backstage` folder of the
[Metalama](https://github.com/metalama/Metalama) repository. Metalama 2026.1 still contains that folder and is still
maintained, so its fixes must be brought to this repository. This document records the last Metalama commit that was
merged, and describes the procedure.

## Last merged point

| Metalama commit | Commit date | Merged into `develop/2026.1` of this repository |
|---|---|---|
| `159c403fe7bebdbb6174e8f92cfe1e1b2dfe223c` | 2026-10-01T12:29:20-07:00 | 2026-10-01 |

The next merge starts from this commit. At each merge, replace this row and move the previous one to the table of
earlier merged points.

### Earlier merged points

| Metalama commit | Commit date | Merged into `develop/2026.1` of this repository |
|---|---|---|
| `b58b2c1748b65f91845df71be4324fb59ed83863` | 2026-09-04T13:42:27+02:00 | Initial mirror (`mirror/metalama-2026.1`), produced by `git filter-repo` |

## Branches

- `develop/2026.1` contains the `Metalama.Backstage` folder of Metalama `develop/2026.1`, with the original paths and
  namespaces. Its tree must always be identical to the tree of that folder at the last merged point. It starts from
  `mirror/metalama-2026.1`, which is an ancestor of `develop/2027.0`.
- `develop/2027.0` receives the changes through an ordinary merge of `develop/2026.1`. Git detects the renamed
  directories and maps the paths.

## Procedure

The commands below are run in the Metalama repository and in this repository. Replace `<last>` with the commit of the
last merged point and `<tip>` with the current tip of `origin/develop/2026.1` of Metalama.

1. In Metalama, fetch `origin` and create the diff of the folder:

   ```powershell
   git diff <last> <tip> -- Metalama.Backstage > upstream.diff
   ```

   If the diff is empty, there is nothing to merge.

2. In this repository, apply the diff to `develop/2026.1`. The option `-p2` removes the `a/Metalama.Backstage/`
   prefix. The diff must apply without conflict, because the tree of `develop/2026.1` is identical to the folder at
   `<last>`.

   ```powershell
   git switch develop/2026.1
   git apply -p2 --index upstream.diff
   ```

3. Verify that the tree is identical to the folder at `<tip>`. The two hashes must be equal.

   ```powershell
   git write-tree                                  # in this repository
   git rev-parse <tip>:Metalama.Backstage          # in Metalama
   ```

4. Commit with the subject `Import Metalama.Backstage from metalama/Metalama develop/2026.1 at <tip>`, and name the
   range and the issues in the body.

5. Create a topic branch from `develop/2027.0` and merge `develop/2026.1` into it with `git merge --no-ff`. Then
   resolve the conflicts, and review the changes that merged without conflict. Git does not detect the following
   problems:

   - A file that Metalama added in a renamed directory is reported as a `file location` conflict. Git proposes the
     renamed directory, which is correct. Stage the file there.
   - The new files use the namespaces of Metalama (`Metalama.Backstage.*`). Replace them with the namespaces of this
     repository (`SharpCrafters.Backstage.*`).
   - An abstract member or an interface member that Metalama added must also be implemented by the types that exist
     only in this repository, for example `LinuxUserInterfaceService`.
   - A reference such as `#2047` in a comment designates an issue of Metalama. Write it as `metalama/Metalama#2047`.

6. Build, run the tests, and update the table of the last merged point in this document in the same pull request.
