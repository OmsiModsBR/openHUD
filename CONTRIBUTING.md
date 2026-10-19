# Contributing to openHUD

Thank you for your interest. openHUD is an OmsiLaunch runtime module (module id `openhud`). This page says how to build and test it, what the documentation rules are, which commit policy applies and what belongs in the repository.

## Building and testing

You need Windows, the .NET SDK (the module targets .NET 6; the .NET 8 SDK builds it), Visual Studio or the Visual Studio Build Tools with the C++ workload (platform toolset v145, x86) and a checkout of [OmsiLaunch](https://github.com/OmsiModsBR/OmsiLaunch) whose bridge serves the protocol the module declares in its manifest (`module.template.json`: bridge protocol 3).

```text
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Build-Module.ps1 -OmsiLaunchRoot <path to OmsiLaunch>
```

The script builds the native component (x86), the managed component and the tests, runs them, and assembles `artifacts\modules\openhud\` with every component pinned by its SHA-256 in `module.json`. Nothing is copied into an OMSI installation, and the tests never start OMSI. If your Visual Studio does not provide toolset v145, pass `-PlatformToolset v143` (or the one you have) to build locally; do not change the toolset named in the project files for that.

To reproduce a release locally (build, archive, checksum, and a check of the archive against the manifest pins):

```text
powershell -NoProfile -ExecutionPolicy Bypass -File tools/New-ReleasePackage.ps1 -OmsiLaunchRoot <path to OmsiLaunch>
```

The result is `artifacts\release\openhud-<version>.zip` and its `.sha256`. The same build, the documentation check and the commit policy run on every pull request ([.github/workflows](.github/workflows)); a change is ready for review when they pass.

Runtime behaviour inside OMSI is not part of the pull request checks. It is validated separately by the maintainers; say in the pull request what your change affects at runtime so that it can be scheduled.

## Documentation

The documentation under `docs/` is part of the product and has two languages: English and Brazilian Portuguese (`docs/pt-BR/`).

- Change a page and its pt-BR counterpart in the same pull request. The pt-BR tree mirrors the English one: the same pages, the same headings in the same order (each with its explicit English anchor line), identical code blocks, and the localization banner carrying the current SHA-256 of the English page.
- A new page is listed in `docs/README.md` and in `docs/pt-BR/README.md`.
- Settings and flags of the manifest template are documented in the settings reference, in both languages, with type, default and bounds.
- Run `powershell -NoProfile -ExecutionPolicy Bypass -File tools/Check-Docs.ps1` before you push. It checks links and anchors, the settings tables, the page index and the pt-BR parity, and writes nothing.
- Add a line to `CHANGELOG.md` for any change a user can notice.

## Commit policy

- Write commit messages in English, in the imperative, saying what changed and why.
- Commit as yourself. Commit messages and pull request descriptions must not name an AI assistant as author, co-author or generator: no `Co-Authored-By` trailer for one, no "Generated with" line, no assistant address. The `commit-policy` check rejects them.
- Keep a pull request to one subject.

## What belongs in the repository

Only finished, reviewed work belongs here: code that builds, has tests where the change is testable, and has its documentation. Experiments, proofs of concept, scratch captures and investigation notes stay out of the repository and out of pull requests; keep them on your machine and describe the finding in the issue or pull request instead.

Do not add OMSI game files, assets or other people's content. Never add code that automates OMSI's interface or that changes its license or add-on ownership checks.

## Licence

openHUD is free software under the [GNU General Public License, version 3](LICENSE). Copyright © 2026 LMonteiro - github.com/lmonteirotech and OmsiModsBR Team - github.com/OmsiModsBR. By contributing you agree that your contribution is licensed under the same terms.

The module uses OmsiLaunch, which is licensed under the LGPL-3.0-only; see `NOTICE.txt` in the module folder.

## Reporting problems

Use the issue forms for bugs and feature requests. For a vulnerability, do not open an issue: follow [SECURITY.md](SECURITY.md).
