# Nexora PUBG Mobile Tool v1.2.0

A major release focused on architectural stability, standalone feature isolation, and performance optimization.

### Key Highlights
* **Feature-First Architecture:** Complete decoupling of core modules (Graphics, Tuning, Network, Optimizer, Shortcuts, and About) into independent, transiently registered UserControls.
* **Lean Shell & Lifecycle:** Slimmed `MainWindow` into a pure navigation and presentation host with explicit lifecycle management.
* **Expanded Verification Suite:** Raised the automated test floor to 519 passing tests with dedicated architecture guards enforcing strict layer boundaries.
* **Streamlined UI Assets:** Fixed relative pack URI resolution for style previews and icons across all standalone feature views.

---

### Integrity & Checksums
For verification and automated in-app updater validation, verify the standalone executable against the following hash:

```text
SHA-256 (Nexora-v1.2.0-win-x64.exe): 9c7a45210997a49acc50325a2d753d2b34f689e9467c0a995af3e417a67e827a
```
