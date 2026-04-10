# THIRD_PARTY_NOTICES

This file describes third-party components that may be bundled with or used by TunnelFlow distributions.

Important:
- This file must match the actual contents of the distribution.
- Only components that are actually bundled with a given release should be listed as bundled.
- If a component is not shipped in a specific release artifact, remove or adjust the relevant section before publishing that artifact.

---

## TunnelFlow

TunnelFlow is distributed under the terms stated in the repository `LICENSE` file.

---

## Bundled or Redistributed Third-Party Components

### 1. sing-box

- Project: sing-box
- Upstream: SagerNet
- Upstream repository: https://github.com/SagerNet/sing-box
- Typical bundled artifact in TunnelFlow distributions: `sing-box.exe`
- Version: `REPLACE_WITH_ACTUAL_BUNDLED_VERSION`
- License: GNU General Public License, version 3 or any later version (GPL-3.0-or-later), according to the upstream project

License note:
TunnelFlow may bundle or redistribute sing-box as a separate third-party component. If a TunnelFlow release includes `sing-box.exe` or other sing-box binaries, the distribution must preserve the applicable upstream license text and comply with the corresponding GPL obligations for that bundled component.

Upstream notice summary:
Copyright (C) 2022 by nekohasekai and contributors.
See the upstream project and bundled license files for details.

---

### 2. Wintun

- Project: Wintun
- Upstream: WireGuard / Wintun
- Upstream repository: https://github.com/WireGuard/wintun
- Typical bundled artifact in TunnelFlow distributions: `wintun.dll`
- Version: `REPLACE_WITH_ACTUAL_BUNDLED_VERSION`
- Licensing: verify against the exact Wintun package included in the release

Packaging note:
Wintun is deployed as a platform-specific `wintun.dll` file. If a TunnelFlow release includes `wintun.dll`, include the corresponding upstream license materials that accompany the exact bundled Wintun package.

Additional source note:
The upstream `api/wintun.h` file is marked with `SPDX-License-Identifier: GPL-2.0 OR MIT`.

---

## Optional Additional Bundled Files

If a release bundles any additional third-party files, such as runtime DLLs, helper executables, drivers, or assets that originate from upstream projects, add them here before publishing.

Suggested entry format:

### Component name
- Project:
- Upstream:
- Upstream repository:
- Bundled artifact(s):
- Version:
- License:
- Notes:

---

## Release Maintainer Checklist

Before publishing a release, verify all of the following:

1. Every third-party component actually bundled in the release artifact is listed here.
2. No component is listed as bundled if it is not present in the release artifact.
3. The version field is replaced with the exact bundled version.
4. The required upstream license text and notices are included where required.
5. This file matches the exact contents of the shipped distribution.

