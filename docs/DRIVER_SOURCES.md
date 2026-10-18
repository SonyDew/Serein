# Official driver sources — checked 2026-10-14

The Drivers → Other sources tab includes these routes. Updated 2026-10-17: shared
device routing uses reviewed IDs, exact manufacturer aliases and bounded model
rules. Computer/board support is separate in WinUI, never an unknown-device fallback.
A support route is not an identified compatible update. No serial upload, scraping
or comparison of unrelated version schemes.

| Source | Verified support route | Selection considerations |
|---|---|---|
| Dell | https://www.dell.com/support/home/en-us?app=drivers | Exact model; optional Service Tag entered by the user on Dell's site |
| Lenovo | https://pcsupport.lenovo.com/us/en/ | Exact model/family; System Update support varies |
| HP | https://support.hp.com/us-en/drivers | Product and OS; business tools have separate coverage |
| ASUS | https://www.asus.com/support/download-center/ | Exact notebook/board model; OEM customizations |
| Acer | https://www.acer.com/us-en/support/drivers-and-manuals | Model/SNID; user chooses applicable package |
| MSI | https://www.msi.com/support/download/ | Exact product; drivers distinguished from BIOS |
| GIGABYTE | https://www.gigabyte.com/Support/Consumer/Download | Board/product model **and revision** |
| ASRock | https://www.asrock.com/support/index.asp?cat=Drivers | Exact board and Windows version |
| Samsung | https://www.samsung.com/us/support/computing/windows-laptops/ | Exact model code and region |
| NVIDIA | https://www.nvidia.com/en-us/drivers/ | GPU, OS and driver branch |
| AMD | https://www.amd.com/en/support | GPU/chipset; notebook OEM guidance first |
| Intel | https://www.intel.com/content/www/us/en/download-center/home.html | Product/OS; DSA does not cover every component |
| Realtek | https://www.realtek.com/download/list | Generic packages may omit OEM audio integrations |
| Qualcomm | https://www.qualcomm.com/drivers | Usually OEM distribution; Snapdragon ARM64 downloads do not fit this x64 target |
| Microsoft Update Catalog | https://www.catalog.update.microsoft.com/ | Manual Hardware ID search, OS/architecture/catalog/INF verification still required |

WUA searches the service assigned by Windows policy and can return no candidates while an OEM has packages. It is not a universal vendor catalog. No generic supported cross-vendor acquisition API was established. Some vendors have individual services (for example [Acer's published service description](https://global-download.acer.com/api/wsgdp.asmx?op=SNModel2DriverList)); no automated vendor-query adapter is claimed here.

## Direct download boundary

Recognition additions2026-10-17: Crucial SSD/Storage Executive, Micron disks and
controllers, Samsung SSD tools, Kingston SSD Manager, Apacer consumer downloads,
TP-Link, Logitech/Razer/Corsair/SteelSeries software, exact ASUS Delta II support.
URLs/evidence: RESEARCH_RESULTS.md and DriverSources.Catalog. Manufacturer aliases
are exact; installed driver publisher never proves hardware identity. No new
download hosts, automatic vendor-version feeds or firmware installation added.

An explicit user-pasted HTTPS link can download to an empty folder from the following exact hosts. Every redirect is checked, the stream is capped at 1 GiB, and SHA256 plus origin paths are recorded. Files are never run or extracted automatically. The recorded hash is a local integrity fingerprint, **not a manufacturer-supplied checksum**. EXE/MSI/CAB/ZIP require manufacturer instructions or manual extraction before the separate INF review.

| Hosts | Primary evidence |
|---|---|
| `dl.dell.com` | [Dell network exposure requirements](https://www.dell.com/support/manuals/en-uk/command-update/dcu_scg/network-exposure?guid=guid-0af26c4d-4c35-416f-9209-64df5011adcd&lang=en-us) |
| `download.lenovo.com` | [Lenovo product guide download](https://download.lenovo.com/pccbbs/thinkcentre_pdf/thinkstation_p620_dash_configuration_guide_v1.3.pdf) |
| `ftp.hp.com` over HTTPS | [HP SoftPaq record](https://ftp.hp.com/pub/softpaq/sp155001-155500/sp155277.html) |
| `dlcdnets.asus.com` | [ASUS support download instructions](https://www.asus.com/us/support/faq/1048540/) |
| `global-download.acer.com` | [Acer service description](https://global-download.acer.com/api/wsgdp.asmx?op=SNModel2DriverList) |
| `download.msi.com` | [MSI audio troubleshooting/download flow](https://www.msi.com/support/technical_details/NB_OS_Audio_Failure) |
| `download.gigabyte.com` | [GIGABYTE product guide](https://download.gigabyte.com/FileList/Manual/VGAGUIDE_web_113R.pdf) |
| `download.asrock.com` | [ASRock driver selection page](https://www.asrock.com/support/index.asp?Model=driver) |
| `downloadmirror.intel.com` | [Intel reference-system guide](https://builders.intel.com/docs/networkbuilders/network-and-edge-virtual-machine-reference-system-architecture-user-guide-1681894704.pdf) |
| `us.download.nvidia.com`, `international.download.nvidia.com` | [NVIDIA driver installation guide](https://docs.nvidia.com/datacenter/tesla/pdf/NVIDIA_Driver_Installation_Quickstart.pdf), [NVIDIA distribution documentation](https://docs.nvidia.com/dgx/archives/dgx-os-server-release-notes/rotate-gpg-keys-server.html) |
| `download.microsoft.com`, `download.windowsupdate.com` | [Microsoft WSUS configuration](https://learn.microsoft.com/en-us/windows-server/administration/windows-server-update-services/deploy/2-configure-wsus) |

Host ownership does not establish a file's driver compatibility or authorize firmware installation. An unsupported CDN fails closed; the official support website remains available. AMD and other unverified direct hosts are intentionally absent. No third-party driver packs or mirrors are included.
