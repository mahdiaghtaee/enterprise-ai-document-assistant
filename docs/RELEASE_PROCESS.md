# Release Process

This project uses small, reviewable pull requests and tagged releases for completed milestones.

## Release Criteria

A release candidate should meet all of the following conditions:

- the milestone behavior is implemented and documented;
- automated tests cover the main success and failure paths;
- the default CI workflow passes;
- Docker Compose configuration and image builds pass;
- public documentation describes implemented behavior separately from planned behavior;
- security and migration implications are documented;
- known limitations are listed in `CHANGELOG.md`.

## Versioning

The project follows semantic versioning:

- **patch**: compatible fixes and documentation corrections;
- **minor**: compatible capabilities such as a new provider or endpoint;
- **major**: incompatible public API, configuration, storage, or deployment changes.

Until the public contracts stabilize, minor releases may still contain explicitly documented migration steps.

## Preparation

1. Confirm that the milestone issues and pull requests are complete.
2. Update `CHANGELOG.md`:
   - move relevant entries out of `Unreleased`;
   - add the release version and date;
   - list added, changed, fixed, security, migration, and known-limitation notes as applicable.
3. Update README, architecture, configuration, and troubleshooting documentation.
4. Run the local verification commands:

```bash
dotnet test tests/api-dotnet/EnterpriseDocumentAssistant.Api.Tests.csproj --configuration Release
docker compose config
docker compose build
```

5. Open a release-preparation pull request and require CI to pass.

## Tag and GitHub Release

After the preparation pull request is merged:

```bash
git checkout main
git pull --ff-only
git tag -a vX.Y.Z -m "Enterprise AI Document Assistant vX.Y.Z"
git push origin vX.Y.Z
```

Create a GitHub Release for the tag with:

- a concise summary;
- major features and fixes;
- migration or configuration changes;
- known limitations;
- links to relevant issues and pull requests;
- verification instructions.

## Post-release Verification

- confirm the tag points to the intended commit;
- confirm the release notes match `CHANGELOG.md`;
- run the documented quick start from a clean checkout;
- create follow-up issues for deferred or discovered work;
- do not edit released behavior claims without recording the correction in the changelog.
