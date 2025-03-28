## Analysis
We are planning to upgrade selected projects to the new .NET framework. The following summarizes the proposed changes across all projects:

### SDK Installation
- Validate that the required SDK for the upgrade is installed on the machine. If not, assist in getting it installed.

### SDK Settings in global.json Files
- Ensure that the SDK version specified in global.json files is compatible with the upgrade.

### Potential Changes
- There are currently no changes required for some projects, but this might change if other projects are modified.

### Project Dependencies
- Note that changes in project dependencies could lead to breaking changes in the code, which will be resolved during the upgrade process.

## Steps
1. Checkout upgrade branch upgrade-to-NET9.
2. Run upgrade using tool run_upgrade.
