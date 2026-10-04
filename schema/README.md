# Drawing-spec contract (helyszinrajz)

This directory holds the single source of truth shared by the AI service and the
AutoCAD Map 3D 2026 plugin: the JSON drawing-spec contract plus a pure
standard-library validator, swappable configuration, a worked sample, and tests.

## What is a drawing spec?

A drawing spec is a JSON document describing a Hungarian electrical connection
site plan (helyszinrajz). The AI service produces it from the e-kozmu base map
PDF and an on-site field sketch; the AutoCAD plugin renders editable geometry
from it. The AI never writes DWG directly.

Fixed conventions baked into the schema:

- Scale is 1:500 (`scale_denominator` is a constant `500`).
- 1 drawing unit = 1 meter. Model coordinates are in meters.
- `georeference` holds EXACTLY 2 reference points mapping image/paper
  coordinates to model meters. Two points fix translation, rotation, and
  uniform scale.

## Files

- `drawing-spec.schema.json` - JSON Schema (draft 2020-12). The contract.
- `config/symbols.config.json` - swappable symbol/layer catalog. Maps each
  `symbol_key` to display name, layer, ACI color, linetype, AutoCAD block name,
  and a fallback geometry hint. Block names are placeholders until the user
  supplies the house `.dwt`/`.dwg`.
- `config/titleblock.config.json` - swappable title-block (rajzpecset) layout:
  field order, Hungarian labels, attribute tags, and paper-mm placement
  fallbacks.
- `samples/sample-lajosmizse.drawingspec.json` - a complete worked example
  using the real Lajosmizse values.
- `validate.py` - the validator (library + CLI).
- `test_validate.py` - the unittest suite.

## How to validate

The validator is pure Python standard library. No `pip install` is required.
If the third-party `jsonschema` package happens to be importable it is used;
otherwise a built-in validator covering the subset of draft 2020-12 used here
runs instead.

```sh
python validate.py samples/sample-lajosmizse.drawingspec.json drawing-spec.schema.json
```

Prints `VALID` and exits `0` when the document conforms; otherwise prints the
error list and exits `1`.

As a library:

```python
from validate import validate_spec

errors = validate_spec(spec_dict, schema_dict)  # [] means valid
```

## How to run the tests

```sh
python -m unittest discover -s . -p 'test_*.py' -v
```

From the workspace root:

```sh
python -m unittest discover -s schema -p 'test_*.py' -v
```

## Swapping configuration

`config/symbols.config.json` and `config/titleblock.config.json` are meant to be
edited (not the schema) when the user supplies their house template. Point the
`block_name` and `attribute_tag` values at the blocks and attributes in the real
`.dwt`/`.dwg`, adjust layers, colors, linetypes, and placement fallbacks, and
the plugin picks up the changes without a schema change.
