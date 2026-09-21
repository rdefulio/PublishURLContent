# Changelog

## 2026-09-21 — Citrix Virtual Apps and Desktops 2402 LTSR

Compatibility and quality updates so the utility runs on 2402 LTSR Delivery Controllers and publishes full-color icons.

### Fixed

- **Silent crash on 2402 LTSR.** The app was an AnyCPU WinExe with Prefer32Bit enabled, so it started as 32-bit (`SysWOW64\WerFault.exe`) and access-violated (`0xc0000005`) while loading 64-bit Citrix PowerShell assemblies. There was no window because this happened in the form constructor. The project now builds as **x64**.
- **Broker SDK load on 2402.** Startup no longer calls `AddPSSnapIn` on every Citrix snap-in (Trust, Host, MCS, Licensing, and others). Those extra snap-ins request `SeTcbPrivilege` and are not used here. The app now loads only **Citrix.Broker.Admin.V2** (snap-in first, then `Citrix.Broker.Commands` / DLL fallback).
- **False “PowerShell command failed” on startup.** Named `Get-PSSnapin -Name` probes set `HadErrors` even when the SDK was present, often with an empty error stream. Discovery now lists registered snap-ins instead of looking up a missing name.
- **NullReferenceException while reading site data.** Delivery group fields such as `Description` and `PublishedName` can be null; those reads are now null-safe.
- **16-color / dithered icons in StoreFront.** Upload went through `Icon.ExtractAssociatedIcon` and `Icon.Save()`, which GDI writes as 4-bit ICO. Icons are now encoded as **32-bit multi-size ICOs** (16–256px, alpha preserved) and that payload is sent to `New-BrokerIcon` without a GDI round-trip.

### Changed

- Target framework is **.NET Framework 4.8**.
- Startup errors show a dialog with details instead of exiting with no UI.
- The in-app image list uses 32-bit color at 32×32 so previews match uploaded icons more closely.

### Notes

- Copy the new **64-bit** `PublishContent.exe` to the Delivery Controller. The previous 32-bit binary will still crash on 2402.
- Icons already stored in the broker stay 16-color until they are uploaded again.
- Run as a Citrix administrator on a machine that has Studio or the Broker PowerShell SDK (the controller itself is the usual place).
