# Lightweight package and installer repair

Remove the three v1.5.3 bundled Workshop assets from new packages and the built-in catalog; keep six generated backgrounds, local imports and Steam discovery. Do not delete source Workshop files. Update documentation and the local-library screenshot; retain prior release history.

The user also reports MSI 2753. The local MSI log records an InstallBackground failure (1722), and installer-service.log reports DebugCleanup.Remove HRESULT 0x80070490. Fix this specific already-missing package-debug-hook cleanup idempotently, after rechecking ownership of any leftover registry entry. Do not suppress unrelated failures.

Replace installed-file custom actions with quoted directory-based executable actions and component-state conditions, preserving after-InstallFiles / before-RemoveFiles ordering. Add a test-only, isolated product namespace and harmless manager probe so install/repair/uninstall actions can execute without configuring real services or touching the real installation.

Verify six defaults even with a stale bundled catalog, package absence of retired media, owned stale-hook cleanup and foreign-hook rejection, isolated MSI install/repair/uninstall, package hashes and metadata. Do not rerun manager resizing/slider tests, restart Codex, or retry installation into the user's real program directory. Release v1.5.4 with main/develop/fix branch and change tags.
