# Driver installation — implemented developer workflow

2026-10-14. Native implementation exists for both paths below; real installation/UAC still requires an isolated Windows VM gate. Read-only review of an existing signed Realtek INF verified catalog, compatible nodes/ranks and equivalent copied staging on the current host. Default tests never install drivers or export the host's driver store.

## Windows Update

1. Explicitly scan Devices, then Find WUA updates. The configured Windows Update/WSUS policy determines availability.
2. Select one update; inspect Hardware ID, provider, source, UpdateID/revision and complete bounded EULA. Firmware/BIOS/UEFI is refused.
3. Explicit confirmation launches the one-shot `Serein.Admin.exe` through normal UAC. Desktop stays standard user; no permanent service.
4. The broker searches the same exact identity and rechecks metadata/source/EULA. Changed revision or license requires new review. Busy WUA or reboot prerequisite blocks the operation.
5. Record relevant installed driver metadata, export third-party drivers, download, then install. Backup or partial/failed download prevents install. Both overall and per-update success codes/HRESULT are checked. No automatic restart.

## Local INF

Choose an extracted, local INF/CAT package. Windows `SetupVerifyInfFile` verifies the INF through its catalog. SetupAPI builds the compatible-driver list for present devices/current OS/architecture and exposes driver rank. The UI shows signer, catalog, candidates and uncertainty; date alone never selects a driver.

Explicit installation copies the reviewed file manifest into administrator-controlled staging, rechecks hashes/signature/compatibility there, exports third-party drivers, then calls fixed System32 PnPUtil `/add-driver <single.inf> /install`. Windows performs its own package checks and selection; a successful command can retain a better-ranked installed driver. The result is labeled staging/selection completed, not proof of changed active version. Rescan Devices to inspect the outcome.

Local limitations: maximum 512 files/128 folders/1 GiB, MAX_PATH INF, no UNC/reparse/alternate-stream members. Firmware, Computer, HDC, SCSIAdapter, Volume, DiskDrive and System classes are refused. Existing devices without a known matching class are not covered by the local matcher; use OEM/Windows guidance. OEM EXE/MSI installers are not executed by this broker. Original download provenance is not proved by a catalog signature. Package payload checks at native commit remain Windows' responsibility.

## Reports, interruption and recovery

Installations require gaming to be disarmed and temporary process/power operations restored. Normal application exit and new tuning are blocked while the driver task runs. The broker process is also checked after an IPC timeout/disconnect, so a still-running commit is not mistaken for completion. If the UI disconnects before commit, no next step starts; an in-progress native commit is not killed. A later failure can leave partial effects and is recorded as such.

`%PROGRAMDATA%\Serein-DriverOperations\<operation-id>` holds durable `result.json` phase history, PnPUtil logs, retained payload and exported third-party INF packages. Broker creates this only for an actual elevated operation, with Administrators/SYSTEM write and Users read access. The default suite creates no such directory. Reports/backups are deliberately preserved by uninstall; review/delete them manually after their value has expired.

There is no automatic driver rollback. Backup does not cover firmware, guarantee reinstallability, or replace a system backup. For a faulty driver, use Windows Device Manager → device → Properties → Driver → Roll Back Driver if Windows offers it, or follow the exact OEM recovery procedure. No force deletion/device disablement or automatic reboot is provided.

IPC accepts only bounded typed WUA/INF requests, exact pipe peer PIDs/lifetimes and the same user SID. Alternate administrator credentials fail closed. The unsigned per-user developer installation is **not** a security boundary against malicious code already running as the current user; production signing/deployment and foreign-account tests remain required.

VM acceptance still required: standard-user scan; actual same-user UAC/cancellation; signed compatible package and wrong-OS/architecture package; successful WUA and INF update; rank retention; native payload rejection; failed backup/download/install; controller disconnect; reboot-required result; independent OS rollback. Use a disposable VM and test device, never the development host.
