# helyszinrajz: AI-assisted electrical site plans for AutoCAD Map 3D 2026

This workspace is a three-component scaffold for AI-assisted generation of
Hungarian electrical connection site plans (**villamos csatlakozasi
helyszinrajz**) in **AutoCAD Map 3D 2026**. From a raster **e-kozmu** (e-utility)
base-map PDF drawn at **1:500**, an on-site field sketch, and a short written
brief, an external AI (Anthropic Claude vision) proposes a structured drawing
spec; the AutoCAD plugin then renders fully editable geometry (symbols, cable
runs, legend, title block, and bill of materials) from that spec. The AI never
writes a DWG file: it emits JSON that a human engineer reviews and the plugin
draws, so a qualified engineer always confirms survey-accurate placement before
the drawing is issued.

## Architecture

```
   e-kozmu base map (PDF, raster 1:500)
   + on-site field sketch (image)
   + written brief (text)
            |
            v
  +-----------------------------+
  | Python AI service           |   ai-service/
  | (Anthropic Claude vision)   |
  +-----------------------------+
            |
            v
   drawing-spec JSON  ----------- validated against ----------->  schema/drawing-spec.schema.json
   (symbols, cable_runs, legend,                                  (JSON Schema draft 2020-12)
    title_block, bom, summary)
            |
            v
  +-----------------------------+
  | C# AutoCAD Map 3D 2026      |   acad-plugin/
  | plugin renders editable     |
  | geometry into model space   |
  +-----------------------------+
            |
            v
   Human engineer reviews and confirms survey-accurate
   placement before the drawing is issued.
```

Key principle: **the AI never writes DWG.** It only produces a validated JSON
proposal. All geometry is created by the plugin as native, editable AutoCAD
entities, and the responsible engineer verifies accuracy against the base map
and the field sketch.

## Components

| Directory | Component | Language / stack |
| --- | --- | --- |
| [`schema/`](schema/) | Drawing-spec JSON Schema contract, pure-stdlib validator, swappable symbol/layer and title-block configs, the Lajosmizse sample spec, and unit tests. | JSON Schema (draft 2020-12) + Python 3.11 (stdlib only) |
| [`acad-plugin/`](acad-plugin/) | AutoCAD Map 3D 2026 managed plugin that consumes a drawing spec and renders editable geometry (raster attach, symbols, cable runs, legend, title block, BOM) via a two-point georeference transform. | C# / .NET 8 (`net8.0-windows`) |
| [`ai-service/`](ai-service/) | Python service that prepares the base map and sketch as vision inputs, builds the Claude prompt, calls the model (SDK or stdlib fallback, with an offline mode), parses the result, and validates it against the schema. | Python 3.11 (stdlib core; `anthropic` optional) |

The shared contract between all three is
[`schema/drawing-spec.schema.json`](schema/drawing-spec.schema.json). The AI
service emits documents that conform to it, and the plugin reads exactly those
fields.

## End-to-end workflow

1. **Gather inputs.** Export the official e-kozmu base map as a PDF at 1:500
   (raster only, no vector geometry). Prepare an on-site field sketch image that
   shows the planned connection layout (cable route, meter cabinet, exploratory
   trench). Write a short brief with the project data (address, hrsz., parties,
   dates).
2. **Generate the drawing spec (AI service).** Run `ai-service` with the base
   map, sketch, and brief. The service sends them to Claude vision and gets back
   a single JSON object. See [`ai-service/README.md`](ai-service/README.md).
3. **Validate the spec (schema).** The AI service validates its output against
   `schema/drawing-spec.schema.json` before writing it. You can re-validate any
   spec with `schema/validate.py`. See [`schema/README.md`](schema/README.md) if
   present, or the files in [`schema/`](schema/).
4. **Render in AutoCAD (plugin).** On a Windows machine with AutoCAD Map 3D
   2026, load the plugin and run its command to attach the raster, apply the
   two-point georeference, and draw the symbols, cable runs, legend, title
   block, and BOM as editable entities. See
   [`acad-plugin/README.md`](acad-plugin/README.md).
5. **Engineer review.** A qualified engineer checks the geometry against the
   base map and the field sketch, adjusts placement as needed, and only then
   issues the drawing. The spec records a confidence note stating that this
   review is required.

## Shared conventions

These are stated identically in the schema, the sample, the C# plugin, and the
Python prompt:

- **Scale is fixed at 1:500.** `scale_denominator = 500` in the project and the
  raster attach; `meretarany = "1:500"` in the summary and title block.
- **Georeference has exactly 2 reference points.** Each maps an image/paper
  coordinate to a model coordinate. Two points fully determine translation,
  rotation, and a single uniform scale (a similarity transform).
- **1 drawing unit = 1 meter.** All model coordinates, vertices, and lengths are
  in meters (`units = "meter"`).
- **Fixed symbol-key set (10 keys).** Defined in
  [`schema/config/symbols.config.json`](schema/config/symbols.config.json) and
  enumerated in the Python prompt: `tervezett_fogy_mero_szekreny`,
  `tervezett_csatlakozo_foldkabel`, `tervezett_kutatoarok`,
  `meglevo_04kv_halozat`, `meglevo_20kv_halozat`, `meglevo_gazvezetek`,
  `meglevo_vizvezetek`, `meglevo_szennyvizvezetek`,
  `meglevo_foldfeletti_hirkozlesi`, `meglevo_foldalatti_hirkozlesi`.
- **Title-block attribute tags (uppercase).** Defined in
  [`schema/config/titleblock.config.json`](schema/config/titleblock.config.json)
  and populated by the plugin: `TARGY`, `RAJZSZAM`, `DATUM`, `KIVITELEZO`,
  `TERVEZO`, `MUSZAKI_RAJZOLO`, `MERETARANY`, `ERINTETT_KOZTERULET`,
  `KABEL_HOSSZ`.

## Swappable house template and configs

The house drawing template (`.dwt`/`.dwg`), the title-block (rajzpecset) layout,
and the symbol/layer catalog are all swappable. The plugin reads
`schema/config/symbols.config.json` and `schema/config/titleblock.config.json`
at run time, so you retarget block names, layers, ACI colors, linetypes,
attribute tags, and fallback positions by editing those JSON files (not the
code). When the user supplies the real house `.dwt`/`.dwg`, drop in the actual
block names and the title-block attribute tags, and the plugin populates the
house title block directly; until then it draws a labeled fallback frame and
fallback geometry.

## Constraints and toolchain

### What runs in this sandbox

- **Schema validation runs here.** `schema/validate.py` validates the sample
  spec against the schema using only the Python standard library.
- **The Python offline / dry-run path runs here.** `ai-service` has an offline
  mode that produces and validates a deterministic bundled sample with no
  network and nothing installed from PyPI.
- **The Python unit tests run here** (standard-library `unittest`).

### What does NOT run in this sandbox

- **The C# plugin cannot be compiled here.** There are no AutoCAD managed
  assemblies (`accoremgd.dll`, `acdbmgd.dll`, `acmgd.dll`,
  `AcMPolygonMgd.dll`, etc.) available, so the project cannot be built in this
  environment. The complete source and the `.csproj` with documented
  `HintPath` references are provided; verify by code review here and build on a
  real AutoCAD machine.
- **Claude cannot be called here.** The sandbox has no external network access
  and no PyPI access, so live Claude requests and `pip install` are not
  possible. The offline mode exists precisely so the pipeline is runnable
  without them.

### What real use requires

- **For the plugin:** Windows + Visual Studio 2022 + AutoCAD Map 3D 2026 with
  its ObjectARX / managed SDK assemblies present. Build with
  `dotnet build acad-plugin/HelyszinrajzPlugin.csproj -c Release` or in Visual
  Studio, then NETLOAD the resulting DLL in AutoCAD.
- **For the AI service:** Python 3.10+ with network access and an Anthropic API
  key in `ANTHROPIC_API_KEY`. The official `anthropic` SDK and a PDF-to-image
  converter are optional (there is a stdlib `urllib` fallback client and you can
  pre-render the base map to an image).

### API key vs. subscription (important)

A **claude.ai "Max" or "Pro" subscription is NOT an API key.** Those plans cover
the claude.ai chat UI only and do not grant programmatic API access. To call the
API you must create a separate API key at
[console.anthropic.com](https://console.anthropic.com) (Settings -> API Keys)
and set it in `ANTHROPIC_API_KEY`. API usage is billed separately from any
claude.ai subscription.

## Quick start (sandbox-runnable parts)

From the workspace root:

```sh
# 1) Validate the sample drawing spec against the schema
python schema/validate.py schema/samples/sample-lajosmizse.drawingspec.json schema/drawing-spec.schema.json
# -> VALID

# 2) Run the unit tests
python -m unittest discover -s schema -p 'test_*.py'
python -m unittest discover -s ai-service -p 'test_*.py'

# 3) Run the AI service offline and validate its output
cd ai-service
python -m drawingspec_ai --offline --out spec.json
python ../schema/validate.py spec.json ../schema/drawing-spec.schema.json
# -> VALID
```

See each component's README for full details:
[`schema/`](schema/), [`acad-plugin/README.md`](acad-plugin/README.md),
[`ai-service/README.md`](ai-service/README.md).
