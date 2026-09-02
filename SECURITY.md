# Security Policy

## Supported Versions

Only the latest public release of TwitchCraft is supported with security updates.

| Version        | Supported |
| -------------- | --------- |
| Latest release | Yes       |
| Older releases | No        |

## Reporting a Vulnerability

Please do **not** publicly post security vulnerabilities in GitHub Issues, Discussions, pull requests, or comments.

If you find a security issue, please report it privately to the project owner.

Security issues may include:

* Exposed Twitch access or refresh tokens
* Exposed RCON passwords
* Exposed configuration files
* Viewer token database leaks
* Statistics database leaks
* Unsafe command execution
* Bugs that could allow unauthorized Minecraft server access
* Bugs that could compromise a user's Twitch account, Minecraft server, or local files

## What to Include

When reporting a vulnerability, please include as much information as possible:

* The TwitchCraft version tested
* Your operating system
* Your Minecraft version, if relevant
* A clear explanation of the issue
* Steps to reproduce the issue
* Screenshots, logs, or error messages, if useful
* Whether any private data, tokens, passwords, or database files were exposed

Please do **not** send real Twitch tokens, RCON passwords, private database files, or personal configuration files unless specifically requested through a private reporting method.

## Response

Valid reports will be reviewed, and confirmed vulnerabilities will be addressed as soon as reasonably possible.

If a vulnerability is confirmed, a patched version may be released, and the issue may be mentioned in the release notes without exposing sensitive details.

## User Responsibility

Users are responsible for keeping their Twitch authorization tokens, RCON passwords, configuration files, databases, and Minecraft server files private.

Do not upload or share the following unless specifically requested through a private reporting method:

* `config.json`
* `viewer_tokens.db`
* `statistics.db`
* `.db`, `.db-shm`, or `.db-wal` files
* `.bak` files
* Twitch access or refresh tokens
* RCON passwords
* Personal Minecraft server files

## Disclaimer

TwitchCraft is provided as-is, without any guarantee that it is free from security issues. Users should review the code, use strong passwords, and avoid sharing private files.
