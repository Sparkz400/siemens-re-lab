# Legal, ethical and security notes

## Authorization

All work in this repository was performed on systems we own or are explicitly authorized to
test. Do not use these notes or tools against third-party systems, customer projects, panels or
networks without written permission.

The lab contains no real PLC, no customer project and no HMI panel. PLCSIM Advanced is used for
PLC behavior; the Unified PC runtime is local.

## No protection bypass

- No attempt is made to bypass passwords, licensing, know-how protection, copy protection,
  signed firmware or safety mechanisms.
- No vendor license keys, certificates or activation data are published.
- No vendor binaries, project archives, runtime packages, panel backups or firmware images are
  distributed here.
- The Test Suite stub only allows the Openness worker to build when the optional Siemens Test
  Suite Openness DLL is absent; Test Suite operations fail closed.

## Trademarks and vendor material

Siemens, SIMATIC, TIA Portal, PLCSIM, WinCC, WinCC Unified and related names are trademarks of
Siemens AG. This project is independent and not affiliated with or endorsed by Siemens.

The GraphQL schema is derived from a licensed local installation. The raw SDL is generated
locally and is not committed; only a curated summary and generator scripts are published. Vendor
file formats and protocols are documented for interoperability and research; consult the
applicable license terms before redistributing derived material.

## Secrets

- Runtime credentials (UMC user, encrypted-transfer password) are not stored in this repo.
- The probe accepts credentials as CLI arguments and keeps tokens in memory.
- TIA MCP stores generated PLC credentials only as DPAPI ciphertext for the current Windows user;
  those files are excluded by `.gitignore`.
- Never commit `.token`, `*.dpapi`, `*.pem`, `*.key`, project archives, runtime packages, logs or
  packet captures from a customer environment.

## Disclosure

If you find a vulnerability in Siemens software while using this material, follow Siemens'
responsible-disclosure process (Siemens ProductCERT) instead of publishing exploit details or
customer data. For issues in this repository's own code, open a private security advisory or
contact the maintainer.

## Public-repo hygiene

Before every push:

```powershell
git status --ignored
git grep -n -I -E "password|passwd|secret|token|BEGIN (RSA|EC|OPENSSH) PRIVATE KEY"
```

Treat generated evidence as sensitive by default; publish only curated summaries and synthetic
example data.
