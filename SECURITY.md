# Security policy

## Reporting a vulnerability

Please report a vulnerability privately, through GitHub's private vulnerability reporting: open the **Security** tab of this repository and choose **Report a vulnerability**. Do not open a public issue or pull request for it, and do not post details in discussions.

Say what you found, the version (the `version` of `module.json`), the OmsiLaunch version, how to reproduce it and what you think the impact is. A report is acknowledged as soon as the maintainers can; a fix is released as a new version of the module with a note in `CHANGELOG.md`, and you are credited if you wish.

## Supported versions

Only the latest released version of openHUD receives fixes.

## Scope

openHUD runs inside the OMSI 2 process, loaded by OmsiLaunch's runtime module bridge, so a flaw in it runs with the rights of the game and of the user who starts it. In scope: the module's own code (the managed and native components), the files it reads and writes, the parsing of the data it reads, and the release archive and its checksum.

Also in scope: the readers of the per-map `openomsi-pausemenu` and `openomsi-realmap` packages, which treat a package as hostile data (path checks, size and count bounds); a way around those bounds is a vulnerability.

What the module never does:

- It makes no network connection and sends nothing off the machine.
- It does not simulate keyboard or mouse input, click on the screen or automate OMSI's interface.
- It does not change OMSI's add-ons, licences or Steam ownership checks, and the build never copies anything into an OMSI installation.
- It does not follow a path from a data file outside the folder that file belongs to.
- It starts no process and writes no file at run time: it reads packages and writes nothing, not even to its data directory.
- On any failure it degrades: the failed part is switched off and OMSI's own pause menu is given back, rather than leaving the game in a changed state ([errors and degradation](docs/reference/errors-and-degradation.md); [safety](docs/concepts/safety.md)).

Out of scope: vulnerabilities in OMSI itself, in Windows, in OmsiLaunch (report those to [OmsiModsBR/OmsiLaunch](https://github.com/OmsiModsBR/OmsiLaunch)) and in content packages made by third parties.
