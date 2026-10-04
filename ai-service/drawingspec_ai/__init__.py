"""drawingspec_ai: AI service that turns an e-kozmu base map plus an on-site
field sketch and a written brief into a validated helyszinrajz drawing spec.

The package is intentionally standard-library only at its core. The official
``anthropic`` SDK is an OPTIONAL dependency (see pyproject.toml extras). When it
is not importable the package falls back to a stdlib ``urllib.request`` HTTP
client, and an offline/dry-run mode returns a deterministic canned sample so the
full build -> parse -> validate -> write pipeline is testable with no network.

Public API::

    from drawingspec_ai import Config, generate_spec, build_client

The output document conforms to ``schema/drawing-spec.schema.json`` (FEAT-001)
and is validated with ``schema/validate.py`` before it is written.
"""

from __future__ import annotations

from .config import Config, MissingApiKeyError
from .clients import (
    ClaudeClient,
    OfflineClaudeClient,
    SdkClaudeClient,
    UrllibClaudeClient,
    build_client,
    is_anthropic_sdk_available,
)
from .pipeline import SpecValidationError, extract_json_object, generate_spec

__all__ = [
    "Config",
    "MissingApiKeyError",
    "ClaudeClient",
    "OfflineClaudeClient",
    "SdkClaudeClient",
    "UrllibClaudeClient",
    "build_client",
    "is_anthropic_sdk_available",
    "SpecValidationError",
    "extract_json_object",
    "generate_spec",
]

__version__ = "0.1.0"
