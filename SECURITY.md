# Security policy

## Reporting

Do not open a public issue for a vulnerability that affects a live system, customer data or an
unpatched Siemens product. Use GitHub's private vulnerability reporting for this repository, or
contact the maintainer directly. For Siemens product issues, follow Siemens ProductCERT's
responsible-disclosure process.

## Scope

This repository contains research notes and client code. It does not ship Siemens binaries,
licenses, credentials, project archives, panel backups, firmware or customer data.

## Secrets

Never commit passwords, tokens, DPAPI blobs, private keys, project archives, runtime packages or
packet captures from a customer environment. The `.gitignore` covers the common cases, but always
review `git status --ignored` before pushing.

## Authorized use

Use these notes and tools only on systems you own or are explicitly authorized to test. The lab
uses a local PLCSIM Advanced instance and a local WinCC Unified runtime.
