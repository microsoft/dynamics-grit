# Security Posture

This document describes the security tooling, controls, and supply-chain
practices applied to the Grammar Import Tool (GrIT) repository. It is the
authoritative answer to questions of the form "what scanning / inventory /
gating do you have in place?" and is kept in sync with the actual repo and
pipeline configuration as they evolve.

For reporting vulnerabilities, see [`SECURITY.md`](../SECURITY.md). This file
documents posture, not contact information.

## Static code analysis

| Tool | Where | What it covers |
|------|-------|----------------|
| GitHub CodeQL (default setup) | GitHub repository | C# semantic analysis on every PR and on push to `main`; alerts surface in *Code scanning* and gate PR merges per branch ruleset (severity *high* or higher). |
| .NET Roslyn analyzers | Every build (local & CI) | `EnableNETAnalyzers=true`, `AnalysisLevel=latest`, `AnalysisModeSecurity=All`. Every CA security rule (`CA21xx`, `CA30xx`, `CA53xx`, etc.) is on. Findings surface as warnings; nullability and disposal warnings (`CS86xx`, `CA2213`) are promoted to errors via `WarningsAsErrors` in `Directory.Build.props`. |
| BinSkim | OneBranch pipeline (1ES PT auto-injected) | Native/managed binary checks. Configured to break the build on findings (`globalSdl.binskim.break = true`). |
| AntiMalware Scanner (Defender) | OneBranch pipeline | Source and output scan for known-bad signatures. |
| PoliCheck | OneBranch pipeline | Disallowed terminology / brand checks. Set to break the build on findings. |
| Guardian (Code Sign Validation, ESLint, ARMory, Accessibility) | OneBranch pipeline | Suite of additional SDL scans run as part of the standard 1ES PT decorator chain. |
| Roslyn analyzers (1ES PT pipeline pass) | OneBranch pipeline | Server-side enforcement of analyzer findings beyond the local build. |

TSA (Trust Services Automation) upload of SDL findings is currently
**disabled** in this repository's OneBranch pipelines
(`globalSdl.tsa.enabled: false`); SDL tools therefore run in `break`-build
mode locally to the pipeline. If/when an official release pipeline is added
that requires TSA archival, flip `tsa.enabled` to `true` in that pipeline
and update this section accordingly.

## Secret detection

| Tool | Where | What it covers |
|------|-------|----------------|
| GitHub Secret Protection | GitHub repository (org-managed) | Push protection blocks commits that contain known-format secrets. Validity checks, generic-password detection (Copilot Secret Scanning), and non-provider patterns are enabled. |
| 1ES Secret Scanning | OneBranch pipeline | Scans the workspace for secrets at build time (`1ESSecretScanning@1`). |
| CredScan / Guardian | OneBranch pipeline | Credential pattern scan over source and configuration files. Suppressions live under `.config/CredScanSuppressions.json` if needed. |

Push protection means the audit trail is not "secrets were caught later" but
"secrets cannot enter the repository in the first place". Bypass is restricted
to users with write access and is logged.

## Authentication to Azure OpenAI

The service authenticates to the Azure OpenAI / Azure AI Foundry endpoint
through `AzureOpenAIClientFactory`, which honours
`GptChat:Grxml:AzureOpenAIAuthMode`. Three modes are supported:

| Mode | Recommended for | Credential used |
|------|-----------------|------------------|
| `ApiKey` (default — legacy) | Local development and tests only | `AzureKeyCredential(AzureOpenAIKey)` |
| `ManagedIdentity` | **Production deployments on Azure** (App Service, AKS, Container Apps, VMSS) | `ManagedIdentityCredential` (user-assigned if `AzureOpenAIManagedIdentityClientId` is set, otherwise system-assigned) |
| `DefaultAzureCredential` | Mixed dev/prod environments where the active credential differs by host | `DefaultAzureCredential` (chains MI, Azure CLI, Visual Studio, etc.) |

### Production setup (recommended)

1. Create or pick a managed identity attached to the workload (system- or
   user-assigned).
2. On the target Azure OpenAI resource, grant that identity the
   **`Cognitive Services OpenAI User`** RBAC role (or `Contributor` if your
   policy demands it — narrower role preferred).
3. Set the following in your environment's `AppSettings.{Env}.json` or via
   the `GPTPrompter_GptChat__Grxml__*` environment variables:

   ```json
   {
     "GptChat": {
       "Grxml": {
         "OpenAI_Provider": "AzureOpenAI",
         "AzureOpenAIEndpoint": "https://<your-resource>.openai.azure.com/",
         "AzureOpenAIDeploymentName": "<your-deployment>",
         "AzureOpenAIAuthMode": "ManagedIdentity",
         "AzureOpenAIManagedIdentityClientId": "<optional user-assigned MI client id>",
         "AzureOpenAIKey": ""
       }
     }
   }
   ```

4. Confirm in pod / app logs that startup prints
   `AzureOpenAIChatService created with endpoint: ..., authMode: ManagedIdentity`
   and that the `AzureOpenAIAuthMode=ApiKey in environment '<Production>'`
   warning is **not** emitted.

### Safety nets

* If `AzureOpenAIAuthMode` is left at the default `ApiKey` **but no
  `AzureOpenAIKey` is provided**, the factory silently promotes the
  credential to `DefaultAzureCredential` (and logs a warning) instead of
  starting up with an empty key. Missing key → secure-by-default fallback,
  not a runtime failure or insecure call.
* On startup, if the resolved configuration has
  `AzureOpenAIAuthMode = ApiKey` while the environment is **not** `Test`,
  `Local`, or `Development`, the host logs a clearly-flagged warning
  pointing operators back to this document. The warning is informational
  (does not block startup) so existing deployments keep running while the
  migration to managed identity is in progress.
* The static API key path is still supported indefinitely for unit tests
  and disconnected local development against the `Stubs` mock service.

## Dependency inventory and supply chain

* **Central Package Management** — every NuGet dependency version lives in
  `Directory.Packages.props` (`ManagePackageVersionsCentrally=true`). Project
  files reference packages without versions, so the resolved version of a
  package is unambiguous across the solution.
* **Per-project lock files** — `RestorePackagesWithLockFile=true` is set in
  `Directory.Build.props`. Each project commits its `packages.lock.json`,
  capturing the full transitive graph (direct + indirect dependencies, exact
  versions, content hashes). Reviewing these files in a PR shows precisely
  which dependencies are introduced or upgraded.
* **CI lock enforcement** — the OneBranch restore step in
  `.pipelines/templates/build-steps.yml` passes
  `-p:ContinuousIntegrationBuild=true` to MSBuild, which engages
  `RestoreLockedMode=true` (see `Directory.Build.props`). Any drift between
  `Directory.Packages.props` and the committed lock files fails the
  pipeline restore instead of silently resolving newer transitive packages.
  The same flag is also passed to the `dotnet restore` invocation in
  `post-build-steps.yml` so the test-coverage rerun honours locked mode too.
* **Component Governance** — auto-injected `ComponentGovernanceComponentDetection`
  step inventories all detected components (NuGet, npm if any, etc.) and feeds
  them to the org-wide CG portal for vulnerability and license tracking.
* **NuGet security analysis** — `nuget-security-analysis` 1ES PT decorator runs
  on every PR and official build, flagging packages with known CVEs.

## Software Bill of Materials (SBOM)

Each **successful** pipeline run generates an **SPDX 2.2 SBOM** for the build
output via the `ManifestGeneratorTask@0` step in
`.pipelines/templates/post-build-steps.yml` (the step is gated on
`succeeded()` so a manifest is only produced when the build output it
describes is itself valid; failed builds intentionally do not publish an
SBOM). The manifest is written under `out/_manifest/spdx_2.2/` and uploaded
as part of the build artifacts so that downstream consumers (release
pipelines, audit tooling) have a signed, machine-readable inventory of every
binary and package shipped with that build.

## Branch protection and merge gating

A repository ruleset named **PR validation** applies to `main` and:

* Requires a pull request before merging.
* Requires at least 2 approving reviews from users with write access.
* Requires the `Dynamics-GrIT.OneBranch.PullRequest` status check to pass
  (PR build green).
* Requires branches to be up-to-date with `main` before merging.
* Restricts deletions of the protected branches.
* Bypass is limited to the `Maintain` role and only for pull-request merges.

CodeQL findings of severity *high* or higher block PR merges via the same
ruleset.

## Out of scope

* **Container image scanning** — GrIT is deployed as an ASP.NET Core service,
  not as a container in production. The `Dockerfile` in the repo is used only
  for developer/local convenience; no image is published to a container
  registry as part of release. Container image scanning therefore does not
  apply to the production deployment.
* **Dynamic Application Security Testing (DAST)** — DAST is tracked as a
  separate, longer-term effort. It will run against staged deployments rather
  than on every PR and is intentionally not part of the build pipeline.

## Auditor cross-reference

The list below maps the audit findings that originally triggered this work to
their current state in this repository:

| Audit finding | Status | Reference |
|---------------|--------|-----------|
| SBOM is not generated | **Closed** | `ManifestGeneratorTask@0` in `.pipelines/templates/post-build-steps.yml` |
| Dependency inventory is incomplete (no lock files / snapshots) | **Closed** | `RestorePackagesWithLockFile=true` in `Directory.Build.props` + committed `packages.lock.json` per project |
| Static code analysis is not consistently enabled | **Closed** | CodeQL default setup + Roslyn `AnalysisModeSecurity=All` + 1ES PT BinSkim/PoliCheck/Guardian on every build |
| Secret scanning is disabled | **Closed** | GitHub Secret Protection (push protection on) + 1ES Secret Scanning in pipeline |
| Static API keys used by default; no documented guidance for EntraID / managed identity | **Closed** | `AzureOpenAIAuthMode` (ApiKey / ManagedIdentity / DefaultAzureCredential) in `AzureOpenAIClientFactory` + secure-by-default fallback + production warning + this document's *Authentication to Azure OpenAI* section |
| Container image scanning is missing | **N/A** | No production container image; see *Out of scope* above |
| Dynamic analysis is not performed | **Tracked separately** | DAST roadmap; see *Out of scope* above |
