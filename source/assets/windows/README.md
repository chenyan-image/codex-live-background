# Windows UI Automation interface metadata

`Interop.UIAutomationClient.dll` contains managed COM interface declarations generated from the Windows `UIAutomationCore.dll` type library. It is not a copy of the Windows runtime or a third-party automation engine. The application embeds these declarations; the UI Automation implementation remains the version included in Windows.

- SHA-256: `52d76ab846939e4d2b75ac6d181e5ec8da87b3743af5e81177cff1f4ffa694db`
- Generator source: `tests/ExportWindowsUiaInterop.cs`
- Native client API: https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-entry-uiautoclients
- Cache API: https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-cachingforclients

The generator uses .NET Framework `TypeLibConverter`, so building does not require downloading an SDK, NuGet package or Python module. The declarations are loaded only by the short-lived Douyin control helper.
