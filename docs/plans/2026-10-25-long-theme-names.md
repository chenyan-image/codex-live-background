# 4096 Unicode character theme names

**Goal:** Accept complete theme and wallpaper display names up to 4096 Unicode code points in the manager, PowerShell lifecycle, package validation and injector.

**Scope:** Keep the v1.5.4 manager and v1.5.4.1 EXE lifecycle. Raise name limits, count surrogate pairs correctly, preserve invalid-name checks, and remove the Steam title truncation. Use the existing hashed IDs and generated media filenames. Raise the bounded library index read/write capacity to accommodate the existing 2000-entry cap with long names.

1. Apply one name limit per runtime and validate before changing active metadata.
2. Exercise actual 4096-character saves/imports and injector loads, 4097 rejections, Unicode names, both package formats, and Steam/library persistence using isolated fixtures.
   Keep the type-filter popup alive until its owner is disposed, fixing disposal during WinForms close handling. Verify repeated item clicks, keyboard-reason dismissals, reopen and owner disposal without repeating resize or slider tests.
3. Build the v1.5.4.2 EXE, verify payload and version metadata, and update README features and limits.
4. Commit with a named change tag, update main/develop and publish the verified EXE without changing the running local installation.
