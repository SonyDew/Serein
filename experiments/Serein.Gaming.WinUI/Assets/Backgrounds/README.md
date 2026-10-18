# Background collection

Ten original abstract backgrounds generated with OpenAI ImageGen on 2026-10-17
for this project, by explicit user request. Four coordinated pairs: lavender satin,
blue glass ribbons, mint aurora and amber dunes. The fifth choice uses ruby red
for dark mode and opalescent pearl for light mode. `*-night.png` and `*-day.png`
are the dark/light versions. No remote image service is used by the application.

The independent fourteen-pattern vector collection is drawn by `PatternBackground.cs`.
It does not rasterize or use these images. User backgrounds are decoded locally
and stored separately in the app's data folder, never in this source directory.
Vector glow and optional compositor animation are independent of this image pack.
