# Publishing FreePDF

Publication is explicit. Ordinary builds and the `Candidate packages` workflow
only produce artifacts. `Publish verified packages` defaults to a verification
run with no NuGet authentication or upload. It never rebuilds release packages.

## One-time NuGet setup

An owner signs in to NuGet.org and creates a Trusted Publishing policy:

| Setting | Value |
| --- | --- |
| Repository owner | `io7` |
| Repository | `FreePDF` |
| Workflow filename | `publish.yml` |
| Environment | `nuget` |
| Package scope | `io7.FreePDF*` |
| Permissions | Publish new packages and new versions |

Select the NuGet user or organization that will own the packages. GitHub
organization membership does not establish NuGet ownership. Set the GitHub
repository Actions variable `NUGET_USER` to the NuGet profile username associated
with that policy, not an email address. The workflow obtains a short-lived API
key; do not paste an API key into issues, chat or repository files.

Official setup: https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing

## Release sequence

1. Merge the reviewed version/release-notes change into `main`, the default
   branch. The version label must be empty for a stable release. Keep source
   changes out of a version promotion.
2. Wait for the latest push runs of `Core build`, `Comparison lab` and
   `Candidate packages` to pass at the exact merged commit.
3. Run `Publish verified packages` on that default-branch commit with the
   successful `Candidate packages` run ID and `publish=false`. Retain its
   `verified-release-<version>` artifact: two nupkgs, their validation receipt
   and SHA256SUMS. The workflow checks source revision, successful consumer
   targets, package IDs, versions, hashes, repository metadata and MIT licence.
4. After the maintainer's publication decision, run the same workflow on the
   same commit and run ID with `publish=true`. Only the default branch can
   publish. The `nuget` environment and NuGet policy must match.
5. Verify both NuGet listings and install from NuGet into a clean package cache.
   Tag the exact verified source `v<version>` and publish GitHub release notes
   with the same nupkgs, validation receipt and SHA256SUMS. NuGet may add its
   repository signature; original artifact hashes describe the uploaded files.

NuGet publication is not atomic across packages. Core is uploaded first, then
MigraDoc. If one upload fails, inspect the feed before retrying. The workflow
does not silently skip an existing version or overwrite one. Keep the original
artifact and resolve any partial publication explicitly.

The 7.0.0 support/migration limits in `support-policy.md` and `migrating-to-7.md`
remain part of the release; a plain version does not add new feature claims.
