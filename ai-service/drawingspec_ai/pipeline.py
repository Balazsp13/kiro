"""End-to-end orchestration: inputs -> Claude -> validated drawing spec.

:func:`generate_spec` prepares the images, builds the prompt, calls the selected
Claude client, extracts the JSON object from the model text (stripping code
fences and surrounding prose), parses it, validates it against
``schema/drawing-spec.schema.json`` using the shared ``schema/validate.py``, and
returns the dict (or raises :class:`SpecValidationError` with the collected
validation errors). The result is never written if validation fails.
"""

from __future__ import annotations

import importlib.util
import json
from pathlib import Path
from typing import Any, Callable, List, Optional

from .clients import build_client
from .config import Config
from .pdf_utils import prepare_image
from .prompt import build_messages, build_system_prompt


# Locate the shared schema directory relative to the repo root
# (<repo>/ai-service/drawingspec_ai/pipeline.py -> <repo>/schema).
_REPO_ROOT = Path(__file__).resolve().parent.parent.parent
_SCHEMA_DIR = _REPO_ROOT / "schema"
_SCHEMA_PATH = _SCHEMA_DIR / "drawing-spec.schema.json"
_VALIDATOR_PATH = _SCHEMA_DIR / "validate.py"


class SpecValidationError(ValueError):
    """Raised when the model output fails schema validation.

    Attributes:
        errors: the collected human-readable validation error strings.
    """

    def __init__(self, errors: List[str]) -> None:
        self.errors = errors
        joined = "\n".join(f"  - {err}" for err in errors)
        super().__init__(
            "Claude output failed drawing-spec schema validation:\n" + joined
        )


def _load_validator() -> Callable[[dict, dict], List[str]]:
    """Import ``validate_spec`` from schema/validate.py by explicit path."""
    spec = importlib.util.spec_from_file_location(
        "drawingspec_schema_validate", str(_VALIDATOR_PATH)
    )
    if spec is None or spec.loader is None:  # pragma: no cover - defensive
        raise RuntimeError(f"Cannot load validator module from {_VALIDATOR_PATH}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module.validate_spec  # type: ignore[attr-defined]


def _load_schema() -> dict:
    with _SCHEMA_PATH.open("r", encoding="utf-8") as handle:
        return json.load(handle)


def extract_json_object(text: str) -> dict:
    """Extract a single JSON object from arbitrary model text.

    Handles responses that wrap the JSON in Markdown code fences (```json ...
    ```), that add prose before/after, or that are already clean JSON. The
    strategy: strip fenced blocks if present, otherwise slice from the first
    '{' to the last '}', then ``json.loads``.

    Raises:
        ValueError: if no JSON object can be found or parsed.
    """
    if not isinstance(text, str) or not text.strip():
        raise ValueError("Model returned empty text; no JSON object to extract.")

    candidate = _strip_code_fences(text).strip()

    # Fast path: already a clean JSON object.
    try:
        parsed = json.loads(candidate)
        if isinstance(parsed, dict):
            return parsed
    except json.JSONDecodeError:
        pass

    # Fallback: slice from first '{' to last '}'.
    start = candidate.find("{")
    end = candidate.rfind("}")
    if start == -1 or end == -1 or end < start:
        raise ValueError("Could not find a JSON object in the model response.")
    snippet = candidate[start : end + 1]
    try:
        parsed = json.loads(snippet)
    except json.JSONDecodeError as exc:
        raise ValueError(
            f"Found a brace-delimited region but it was not valid JSON: {exc}"
        ) from exc
    if not isinstance(parsed, dict):
        raise ValueError("Extracted JSON is not an object.")
    return parsed


def _strip_code_fences(text: str) -> str:
    """Return the content of the first Markdown code fence, else the input."""
    marker = "```"
    first = text.find(marker)
    if first == -1:
        return text
    # Move past the opening fence and an optional language tag on the same line.
    line_end = text.find("\n", first)
    if line_end == -1:
        return text
    rest = text[line_end + 1 :]
    closing = rest.find(marker)
    if closing == -1:
        return rest
    return rest[:closing]


def validate_spec_dict(spec: dict) -> List[str]:
    """Validate a spec dict against the shared schema; return error list."""
    validate_spec = _load_validator()
    schema = _load_schema()
    return validate_spec(spec, schema)


def generate_spec(
    base_map_path: Optional[str],
    sketch_path: Optional[str],
    brief_text: str,
    config: Config,
    offline: bool = False,
    out_path: Optional[str] = None,
) -> dict:
    """Generate, validate, and optionally write a drawing spec.

    Args:
        base_map_path: path to the e-kozmu base map PDF or image.
        sketch_path: optional path to the field sketch image.
        brief_text: the written task brief.
        config: runtime :class:`Config`.
        offline: when True, use the canned sample instead of calling the API.
        out_path: when given, write the validated spec JSON to this path.

    Returns:
        The validated drawing-spec dict.

    Raises:
        SpecValidationError: if the model output fails schema validation.
    """
    system = build_system_prompt()

    if offline and not base_map_path:
        # Offline mode exercises the full extract/parse/validate/write path
        # without requiring real input files or any network. The canned client
        # ignores the message content, so a minimal placeholder is fine.
        messages: List[dict] = [
            {"role": "user", "content": [{"type": "text", "text": brief_text}]}
        ]
    else:
        if not base_map_path:
            raise ValueError(
                "base_map_path is required for a live request. Provide "
                "--base-map, or use --offline to run with the bundled sample."
            )
        base_map_image = prepare_image(base_map_path)
        sketch_image = prepare_image(sketch_path) if sketch_path else None
        messages = build_messages(brief_text, base_map_image, sketch_image)

    client = build_client(config, offline=offline)
    response_text = client.send_messages(system, messages)

    spec = extract_json_object(response_text)

    errors = validate_spec_dict(spec)
    if errors:
        raise SpecValidationError(errors)

    if out_path:
        write_spec(spec, out_path)

    return spec


def write_spec(spec: dict, out_path: str) -> None:
    """Write ``spec`` as pretty-printed UTF-8 JSON to ``out_path``."""
    path = Path(out_path)
    if path.parent and not path.parent.exists():
        path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8") as handle:
        json.dump(spec, handle, ensure_ascii=False, indent=2)
        handle.write("\n")
