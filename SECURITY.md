# Security Policy

## Supported versions

Lodestone is pre-1.0; only the latest release receives security fixes.

## Reporting a vulnerability

**Please do not open a public issue for security vulnerabilities.**

Report privately via GitHub's [Report a vulnerability](../../security/advisories/new) form, or email
**podinatubie@gmail.com** with the details and reproduction steps. You'll get an acknowledgement
within a few days, and we'll coordinate a fix and disclosure timeline with you.

## Scope notes

- **Supporter codes** are verified offline with **ECDSA**. The app embeds only the **public** key;
  the private signing key never ships and is not in this repository. A leaked public key does not let
  anyone forge codes. If you believe the *private* key has been exposed, treat it as critical and
  report it privately.
- **Mod downloads** are verified against the SHA-512 published by the source (Modrinth) before they
  are placed into the game folder.
- **Mod descriptions** are rendered in a WebView2 with JavaScript disabled and a strict
  Content-Security-Policy. Reports of CSP bypass or script execution from untrusted descriptions are
  in scope.

Thank you for helping keep Lodestone and its users safe.
