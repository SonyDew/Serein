# Runtime components

- .NET 10 runtime and C#/WinRT projections: Microsoft, MIT and included third-party notices.
  https://github.com/dotnet/runtime/blob/main/LICENSE.TXT
- Windows App SDK 2.5.1 and WinUI 3: Microsoft, MIT; distributable runtime components
  are copied by the official NuGet build targets.
  https://github.com/microsoft/WindowsAppSDK/blob/main/LICENSE
  https://github.com/microsoft/microsoft-ui-xaml/blob/main/LICENSE
- Windows SDK BuildTools 10.0.28000.2705: build-time tools only; Microsoft license
  terms apply. No build tools are distributed with the application.
- Windows SDK .NET projections: Microsoft Windows SDK terms. The publisher's
  original package metadata with its license URL is included in `licenses`.
  https://aka.ms/WinSDKLicenseURL
- AMD ADLX / Ryzen Master Monitoring: external installed system components, not
  included. Serein reuses the core's verified read-only interfaces. CPU connection
  is explicit; the WinUI process remains a standard user.
- Segoe/Fluent glyphs use fonts provided by Windows; font files are not bundled.

Keep runtime LICENSE/NOTICE files from the self-contained output with the app.
