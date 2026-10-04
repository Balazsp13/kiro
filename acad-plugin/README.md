# HelyszinrajzPlugin (AutoCAD Map 3D 2026)

A .NET 8 managed plugin for **AutoCAD Map 3D 2026** that reads a drawing-spec
JSON (conforming to `schema/drawing-spec.schema.json`) and renders real,
editable geometry for a Hungarian electrical connection site plan
(helyszinrajz): the e-kozmu base-map raster, planned symbols, cable runs,
labels, the legend (jelmagyarazat), the title block (rajzpecset), and the
material list (anyagjegyzek / BOM).

The AI service never writes DWG. It emits JSON validated against the shared
schema, and this plugin turns that JSON into editable AutoCAD entities so a
human engineer can review and finish the drawing.

## Important: cannot be built without AutoCAD

This project references the AutoCAD managed assemblies (`accoremgd.dll`,
`acdbmgd.dll`, `acmgd.dll`, and optionally `AcMPolygonMgd.dll` / `AdWindows.dll`).
Those assemblies are **not redistributable** and are provided **only** by a local
installation of AutoCAD Map 3D 2026. Therefore:

- The plugin **only builds on Windows** with AutoCAD Map 3D 2026 installed.
- It **cannot be compiled** on a machine (or CI sandbox) that lacks those
  assemblies. A `dotnet build` there fails with missing-reference errors, which
  is expected.

## Prerequisites

- Windows 10/11 (64-bit)
- AutoCAD Map 3D 2026 installed (ships the .NET 8 managed API)
- .NET 8 SDK
- Visual Studio 2022 (optional, for IDE builds)
- The ObjectARX / managed SDK assemblies that come with the AutoCAD install

## Pointing the build at the AutoCAD assemblies

The `.csproj` resolves the AutoCAD references through the overridable MSBuild
property `AutoCADSdkDir`, which defaults to:

```
C:\Program Files\Autodesk\AutoCAD 2026
```

If your AutoCAD assemblies live elsewhere, override it. The references use
`CopyLocal = false` so the host AutoCAD process supplies them at run time.

### Build with the .NET CLI

```powershell
cd acad-plugin
dotnet build HelyszinrajzPlugin.csproj -c Release -p:AutoCADSdkDir="C:\Program Files\Autodesk\AutoCAD 2026"
```

The output DLL is `bin\Release\HelyszinrajzPlugin.dll`.

### Build in Visual Studio 2022

1. Set an environment variable `AutoCADSdkDir` to your AutoCAD install path, or
   edit the `<AutoCADSdkDir>` default in `HelyszinrajzPlugin.csproj`.
2. Open the project, select the **Release** configuration, and build.

## Loading and running in AutoCAD Map 3D 2026

1. Start AutoCAD Map 3D 2026 and open (or create) a drawing. If you have the
   house template `.dwt/.dwg` with the `RAJZPECSET` title block and the symbol
   blocks (see `schema/config/symbols.config.json`), start from that; otherwise
   the plugin draws documented fallback geometry.
2. Run the `NETLOAD` command and select `HelyszinrajzPlugin.dll`.
3. Run the command:

   ```
   HELYSZINRAJZ_IMPORT
   ```

4. When prompted, choose the drawing-spec JSON (for example
   `schema/samples/sample-lajosmizse.drawingspec.json`).

The plugin loads the config files, opens a single transaction, and renders in
this order:

```
layers -> raster -> symbols -> cables -> labels -> legend -> titleblock -> bom
```

then commits and prints a summary to the command line.

### Config file resolution

`HELYSZINRAJZ_IMPORT` looks for `symbols.config.json` and
`titleblock.config.json` in this order:

1. `<spec-dir>/config/`
2. `<spec-dir>/`
3. `<plugin-dir>/config/`
4. `<plugin-dir>/`

Copy `schema/config/*.json` next to the spec (or beside the DLL) so they are
found. Edit those config files (not the schema) to retarget block names,
layers, ACI colors, and linetypes when the house template is supplied.

### Base-map raster

The `raster.file` path in the spec is resolved relative to the spec JSON when it
is not absolute. PDFs are attached as a PDF underlay; raster images (png/jpg/tif)
as a `RasterImage`. The base map is placed on the dedicated, locked layer
`ALATERKEP-RASZTER`. A missing file is logged and skipped, not fatal.

## Georeference and scale

Scale is fixed at **1:500** and **1 drawing unit = 1 meter**. The two
`georeference` points define a similarity transform (translation + rotation +
one uniform scale) that maps the **raster base map's** image/paper coordinates
onto model meters. See `src/GeoReferenceTransform.cs` for the two-point math.

**Which coordinates get transformed.** The transform applies **only** to the
raster base map (`RasterRenderer`): it supplies the underlay's position,
rotation, and uniform scale. Everything else (symbol positions, cable vertices,
label positions, and the legend / title-block / BOM placements) is authored in
**model meters** per the schema and is placed **directly**, without the
transform. This is deliberate: the two reference points exist to pin the
image/paper-space base map into the meter model, not to rescale geometry that is
already in meters. Running already-in-meters geometry through the transform
would double-count the image-to-model scale and shrink the plan relative to the
base map. For the Lajosmizse sample the transform's scale is 0.5 (image delta
200 maps to model delta 100), so the base map underlay is attached at scale 0.5
while the drawn 15 m cable stays 15 model units long.

**Cable length annotation.** Each cable run is labelled with the length of the
**actual drawn polyline**, so the annotation can never contradict the geometry.
`cable_runs[].length_m` in the schema remains an optional authored hint; if it
is supplied and disagrees with the drawn length, the plugin logs a warning and
still annotates from the drawn geometry.

### Verifying the transform math (runs without AutoCAD)

`GeoReferenceTransform` is pure 2-point similarity math. The AutoCAD-independent
form of that math, plus a self-contained test harness, lives under
`tests/GeoReferenceTransform.Tests/`. It references no `Autodesk.*` type, so it
builds and runs anywhere the .NET SDK is installed (including CI sandboxes
without AutoCAD):

```bash
cd acad-plugin/tests/GeoReferenceTransform.Tests
dotnet run -c Release
```

It asserts (a) the sample's two points yield scale 0.5 and rotation 0, and (b) a
round trip maps each reference image point onto its model point. The harness
exits non-zero on failure and is **not** referenced by the main plugin project.

## Project layout

```
acad-plugin/
  HelyszinrajzPlugin.csproj      net8.0-windows class library, AutoCAD refs via HintPath
  src/
    Commands.cs                  HELYSZINRAJZ_IMPORT command + renderer orchestration
    GeoReferenceTransform.cs     2-point image/paper -> model-meter transform
    LayerService.cs              ensures layers (name, ACI color, linetype)
    Model/
      DrawingSpec.cs             POCOs mirroring the schema
      DtoPoint.cs                DTO 2D point/vertex (separate from Point3d)
    Config/
      JsonConfig.cs              shared System.Text.Json options
      SpecLoader.cs              drawing-spec loader
      SymbolCatalog.cs           symbols.config.json loader + lookup
      TitleBlockLayout.cs        titleblock.config.json loader
    Rendering/
      RenderContext.cs           shared db/transaction/config state
      RasterRenderer.cs          base-map PDF/image attach at 1:500 (locked layer)
      SymbolRenderer.cs          block insert or fallback marker
      CableRenderer.cs           cable-run polylines + labels + length
      LabelRenderer.cs           free-text annotations
      LegendRenderer.cs          jelmagyarazat column
      TitleBlockRenderer.cs      populate RAJZPECSET attributes or fallback frame
      BomRenderer.cs             AutoCAD Table for the anyagjegyzek
  tests/
    GeoReferenceTransform.Tests/ AutoCAD-free transform math + console test harness
```

## Fallback title block (placeholder)

When the house `.dwt` and its real `RAJZPECSET` block are present, the plugin
fills the real block's attributes by tag (from `titleblock.config.json`). When
they are absent, `TitleBlockRenderer.DrawFallback` draws a **schematic
placeholder** frame with the field labels and values. That placeholder is a
deliberate stand-in until the house template arrives: it is not drawn to a real
sheet layout, and the config's `fallback_xy_mm` positions (paper millimetres)
are used directly as model-space offsets (1 model unit stands in for 1 paper mm
inside the frame). The frame is sized to enclose the field text. Do not treat
the fallback block's size or placement as survey- or sheet-accurate; it exists
only so the import is never silently missing a title block.

## Notes

- No third-party NuGet packages: JSON uses `System.Text.Json` from the .NET 8
  runtime.
- The AI output is a draft proposal. A human engineer must confirm
  survey-accurate placement against the e-kozmu base map and the on-site field
  sketch before the drawing is issued.
