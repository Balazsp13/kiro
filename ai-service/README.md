# drawingspec-ai

Python service that turns an **e-kozmu** (e-utility) base map, an on-site field
sketch, and a short written brief into a **helyszinrajz drawing-spec JSON** using
Anthropic Claude vision. The output is validated against the shared schema
(`../schema/drawing-spec.schema.json`) before it is written. The AutoCAD Map 3D
2026 plugin then renders editable geometry from that spec.

The package core is **standard-library only**. The official `anthropic` SDK and
PDF-to-image converters are optional. When the SDK is not installed the service
falls back to a stdlib `urllib.request` HTTP client, and an offline/dry-run mode
returns a deterministic bundled sample so the whole pipeline is runnable with no
network and nothing installed from PyPI.

## Pipeline

```
base map (PDF/image) + field sketch (image) + brief (text)
    -> prepare base64 vision blocks (pdf_utils)
    -> build system prompt + user message (prompt)
    -> Claude client (SDK / urllib / offline)  (clients)
    -> extract JSON object from model text (strip fences + prose)
    -> validate against drawing-spec.schema.json  (../schema/validate.py)
    -> write spec.json   (pipeline)
```

## Run offline (works in this sandbox)

No network and no API key required. Produces and validates the bundled sample
(Lajosmizse) spec:

```sh
cd ai-service
python -m drawingspec_ai --offline --out spec.json
```

You can confirm the written file validates with the shared validator:

```sh
python ../schema/validate.py spec.json ../schema/drawing-spec.schema.json
# -> VALID
```

`--dry-run` is an alias for `--offline`.

## Run live (on your machine)

1. Install Python 3.10 or newer.
2. Set your Anthropic API key:

   ```sh
   export ANTHROPIC_API_KEY="sk-ant-..."
   ```

3. (Optional) Install the official SDK and a PDF converter. The service works
   without them (stdlib `urllib` client), but the SDK is used automatically when
   present, and a converter is needed to send a PDF base map directly:

   ```sh
   pip install anthropic          # optional live backend
   pip install pymupdf            # optional PDF -> image converter
   ```

   In restricted networks `pip install` may be unavailable. In that case either
   pre-render the base map PDF to a PNG/JPG and pass the image, or run offline.

4. Generate a spec:

   ```sh
   python -m drawingspec_ai \
       --base-map ekozmu.pdf \
       --sketch field-sketch.png \
       --brief '6050 Lajosmizse, 0125/2 hrsz. villamos energia ellatas' \
       --out spec.json
   ```

   `--brief` accepts literal text or `@path` to read the brief from a file.

## API key vs. subscription (important)

A **claude.ai "Max" or "Pro" subscription is NOT an API key.** Those plans cover
the claude.ai chat UI only. To call the API programmatically you must create an
API key at [console.anthropic.com](https://console.anthropic.com) (Settings ->
API Keys) and set it in `ANTHROPIC_API_KEY`. API usage is billed separately from
any claude.ai subscription. The key is never logged or printed by this service.

A missing API key raises a clear, actionable error **only** when a live call is
attempted. Offline mode never needs a key.

## Configuration

Resolved from (lowest to highest priority) built-in defaults, an optional
`--config` JSON file, environment variables, then CLI overrides.

| Setting | Env var | Config key | CLI | Default |
| --- | --- | --- | --- | --- |
| API key | `ANTHROPIC_API_KEY` | `api_key` | - | (none) |
| Model | `ANTHROPIC_MODEL` | `model` | `--model` | `claude-3-7-sonnet-20250219` |
| Max tokens | `ANTHROPIC_MAX_TOKENS` | `max_tokens` | - | `8192` |
| Base URL | `ANTHROPIC_BASE_URL` | `base_url` | - | `https://api.anthropic.com` |

The default model is a current vision-capable Claude model. Point `--model` (or
`ANTHROPIC_MODEL`) at any newer vision model your account can access.

Example `config.json`:

```json
{
  "model": "claude-3-7-sonnet-20250219",
  "max_tokens": 8192,
  "base_url": "https://api.anthropic.com"
}
```

## Fixed drawing rules enforced by the prompt

- Scale fixed at **1:500** (`scale_denominator = 500`).
- **Exactly 2** georeference points mapping image/paper coordinates to model
  coordinates, so **1 drawing unit = 1 meter**.
- Symbol keys come from the fixed set defined in
  `../schema/config/symbols.config.json`.
- The output is a **draft proposal**: a human engineer confirms survey-accurate
  placement before issue.

## Tests

Standard-library `unittest`, no network, nothing installed from PyPI:

```sh
cd ..            # repo root
python -m unittest discover -s ai-service -p 'test_*.py' -v
```

## Layout

```
ai-service/
  drawingspec_ai/
    __init__.py       public API
    config.py         Config dataclass (env/file), API-key handling
    clients.py        ClaudeClient ABC + SDK / urllib / offline backends + factory
    prompt.py         system prompt + user message (text + base64 vision blocks)
    pdf_utils.py      PDF first page / image -> base64 ImageBlock
    pipeline.py       orchestration: build -> call -> extract -> validate -> write
    cli.py            argparse CLI
    __main__.py       `python -m drawingspec_ai`
  pyproject.toml      metadata; anthropic/pymupdf are OPTIONAL extras
  test_pipeline.py    unittest suite
  README.md           this file
```
