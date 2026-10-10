# Security Policy

## Supported versions

Security fixes are released for the latest major version only.

| Version | Supported |
|---|---|
| 6.x | Yes |
| 5.x and older | No |

## Reporting a vulnerability

Please do not open a public issue for a security problem.

Report it privately through GitHub: open the [Security tab](https://github.com/oluciano/NexJob/security) of this repository and choose **Report a vulnerability**.

Include, when you can:

- the affected package and version;
- what an attacker can do, and the conditions needed;
- the steps or a minimal project to reproduce it.

## What to expect

NexJob is maintained by one person, so the times below are goals, not guarantees.

- Acknowledgement of your report within 7 days.
- An assessment and a plan within 14 days.
- A fixed release and a security advisory once the fix is available. You are credited in the advisory unless you prefer not to be.

## Scope

In scope: the `NexJob*` packages published from this repository, including the dashboard and its authorization hooks.

Out of scope: vulnerabilities in your own jobs, in the broker or database you connect to, or in a dashboard exposed without an authorization handler (see the dashboard documentation for how to protect it).
