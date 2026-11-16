---
name: upgrade-nuget-packages
description: Upgrade all centrally managed NuGet packages to their latest stable versions, validate the solution, and open a pull request.
---

# Upgrade NuGet packages

Use this skill when asked to upgrade or update the repository's NuGet packages. Complete the entire upgrade in a new branch and create a pull request only after all upgrades and validation succeed.

## Repository context

- `DistributedNonce.sln` contains the library and test projects. Both currently target `net10.0`; check the project files and installed SDK rather than relying on older README requirements.
- `Directory.Packages.props` centrally manages package versions. The project files contain versionless `PackageReference` entries. Check for any other package declarations or NuGet configuration added since this skill was written.
- The NUnit integration suite uses Testcontainers and requires a working Docker daemon and access to `redis:latest`. Do not skip those tests or substitute mocked tests for them.

## Workflow

1. Inspect the worktree and current branch. Preserve any existing changes; if they might be included in the upgrade or prevent a clean branch, stop and ask how to proceed. Identify the intended PR base branch, fetch it when permitted, and create a uniquely named branch from its up-to-date tip before changing packages. Do not overwrite or force-push an existing branch.
2. Inventory every directly declared and centrally managed NuGet package, including any entries not currently referenced by a project. Using the repository's configured NuGet sources, determine the latest **stable** version of each package, including major releases. Include prereleases only if explicitly requested. Do not silently leave a package behind because a newer release is incompatible or a source is unavailable: report the blocker and do not create a PR. Do not add direct references or pins just to upgrade transitive dependencies; let restore resolve them.
3. Update all declarations to those versions, preserving central package management. The .NET SDK's `dotnet package update --project DistributedNonce.sln` can update project references; check `dotnet package update --help` first and inspect `Directory.Packages.props` afterward, since solution-based updates may not cover unreferenced central entries. Update any remaining central declarations explicitly using versions verified from the configured sources. Do not downgrade packages. Make only compatibility changes needed for the upgrades, preserving the public API and nonce/locking behavior; if that is not possible without a broader change, stop and report it. Recheck the complete inventory against the latest versions so a partial upgrade is not treated as success.
4. If every package was already current and no changes are needed, report that and stop without creating an empty commit or PR.
5. Check `docker info` before testing. Use an already running daemon; do not start or alter shared infrastructure without authorization. If Docker, the required SDK, package sources, or the Redis image are unavailable, report the blocker and do not create a PR.
6. From the repository root, run the CI-equivalent commands:

   ```bash
   dotnet restore DistributedNonce.sln
   dotnet build DistributedNonce.sln --configuration Release --no-restore
   dotnet test DistributedNonce.sln --configuration Release --no-build --verbosity normal
   ```

   Confirm that the integration tests actually ran and passed, with no failed or skipped tests. Resolve upgrade-related build or test failures and repeat validation. Never create a PR on a failed, skipped, or unverified run.
7. Review the diff for unintended changes and verify that every declared package is at its verified latest version. Commit only the upgrade and required compatibility changes, following repository and session commit conventions. Push the new branch, then create a PR against the identified base using the available approved GitHub workflow. Check for a PR template and follow it if present. Summarize package version changes, any compatibility changes, and the validation results in the PR.
8. If any step cannot be completed, state what succeeded and the exact blocker. Do not claim a PR was created unless creation succeeded.
