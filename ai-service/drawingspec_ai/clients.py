"""Pluggable Claude clients.

A :class:`ClaudeClient` sends a system prompt plus a list of Anthropic Messages
API ``messages`` and returns the model's text response. Three backends:

* :class:`SdkClaudeClient` wraps the official ``anthropic`` SDK, used only when
  that package is importable.
* :class:`UrllibClaudeClient` POSTs to the Messages API with stdlib
  ``urllib.request`` (x-api-key + anthropic-version headers), so the package has
  zero hard third-party dependencies and still works where PyPI is unavailable.
* :class:`OfflineClaudeClient` returns a canned response (the bundled sample
  spec) for dry-run/offline testing with no network.

:func:`build_client` is the factory: offline -> OfflineClaudeClient; otherwise
SDK if available else urllib.

Message shape used throughout (Anthropic Messages API ``content`` blocks)::

    {"role": "user", "content": [
        {"type": "text", "text": "..."},
        {"type": "image", "source": {"type": "base64",
            "media_type": "image/png", "data": "<base64>"}},
    ]}
"""

from __future__ import annotations

import importlib.util
import json
import urllib.error
import urllib.request
from abc import ABC, abstractmethod
from pathlib import Path
from typing import Any, List

from .config import Config


# Path to the bundled canned sample used by the offline client. It lives in the
# shared schema directory (../schema/samples) relative to the repo root.
_REPO_ROOT = Path(__file__).resolve().parent.parent.parent
_SAMPLE_SPEC_PATH = (
    _REPO_ROOT / "schema" / "samples" / "sample-lajosmizse.drawingspec.json"
)


def is_anthropic_sdk_available() -> bool:
    """Return True if the optional ``anthropic`` SDK can be imported."""
    return importlib.util.find_spec("anthropic") is not None


class ClaudeClient(ABC):
    """Abstract base for a Claude backend.

    Implementations must send the given system prompt and messages and return
    the model's text output as a single string.
    """

    @abstractmethod
    def send_messages(self, system: str, messages: List[dict]) -> str:
        """Send ``messages`` with the ``system`` prompt; return response text."""
        raise NotImplementedError


class OfflineClaudeClient(ClaudeClient):
    """Offline/dry-run backend that returns a deterministic canned response.

    The canned response is the bundled Lajosmizse sample spec, returned as a
    JSON string so the rest of the pipeline (extract -> parse -> validate ->
    write) runs exactly as it would for a live response. No network is used.
    """

    def __init__(self, canned_text: str | None = None) -> None:
        if canned_text is None:
            canned_text = _SAMPLE_SPEC_PATH.read_text(encoding="utf-8")
        self._canned_text = canned_text

    def send_messages(self, system: str, messages: List[dict]) -> str:
        # Return the canned sample verbatim; a live model would return the JSON
        # (optionally wrapped in prose / code fences, which the pipeline strips).
        return self._canned_text


class SdkClaudeClient(ClaudeClient):
    """Backend using the official ``anthropic`` SDK (optional dependency)."""

    def __init__(self, config: Config) -> None:
        if not is_anthropic_sdk_available():  # pragma: no cover - guarded by factory
            raise RuntimeError(
                "The 'anthropic' SDK is not installed. Install it with "
                "`pip install anthropic` or use the urllib client / offline mode."
            )
        import anthropic  # type: ignore

        self._config = config
        api_key = config.require_api_key()
        self._client = anthropic.Anthropic(
            api_key=api_key, base_url=config.base_url
        )

    def send_messages(self, system: str, messages: List[dict]) -> str:
        response = self._client.messages.create(
            model=self._config.model,
            max_tokens=self._config.max_tokens,
            system=system,
            messages=messages,
        )
        return _join_text_blocks(_sdk_content_to_blocks(response))


class UrllibClaudeClient(ClaudeClient):
    """Backend using stdlib ``urllib.request`` (no third-party dependency)."""

    def __init__(self, config: Config) -> None:
        self._config = config
        # Validate up front so a missing key fails clearly on a live attempt.
        self._api_key = config.require_api_key()

    def send_messages(self, system: str, messages: List[dict]) -> str:
        url = self._config.base_url.rstrip("/") + "/v1/messages"
        payload = {
            "model": self._config.model,
            "max_tokens": self._config.max_tokens,
            "system": system,
            "messages": messages,
        }
        body = json.dumps(payload).encode("utf-8")
        request = urllib.request.Request(url, data=body, method="POST")
        request.add_header("content-type", "application/json")
        request.add_header("x-api-key", self._api_key)
        request.add_header("anthropic-version", self._config.anthropic_version)

        try:
            with urllib.request.urlopen(request) as response:
                raw = response.read().decode("utf-8")
        except urllib.error.HTTPError as exc:  # pragma: no cover - needs network
            detail = exc.read().decode("utf-8", errors="replace")
            raise RuntimeError(
                f"Anthropic API HTTP error {exc.code}: {detail}"
            ) from exc
        except urllib.error.URLError as exc:  # pragma: no cover - needs network
            raise RuntimeError(
                f"Could not reach the Anthropic API at {url}: {exc.reason}"
            ) from exc

        data = json.loads(raw)
        return _join_text_blocks(data.get("content", []))


def build_client(config: Config, offline: bool) -> ClaudeClient:
    """Select a Claude backend.

    offline True -> :class:`OfflineClaudeClient` (no key, no network required).
    Otherwise prefer :class:`SdkClaudeClient` when ``anthropic`` is importable,
    else fall back to :class:`UrllibClaudeClient`.
    """
    if offline:
        return OfflineClaudeClient()
    if is_anthropic_sdk_available():
        return SdkClaudeClient(config)
    return UrllibClaudeClient(config)


def _join_text_blocks(content_blocks: List[Any]) -> str:
    """Concatenate the ``text`` fields of Messages API content blocks."""
    parts: list[str] = []
    for block in content_blocks:
        if isinstance(block, dict):
            if block.get("type") == "text" and isinstance(block.get("text"), str):
                parts.append(block["text"])
        else:
            text = getattr(block, "text", None)
            if isinstance(text, str):
                parts.append(text)
    return "".join(parts)


def _sdk_content_to_blocks(response: Any) -> List[Any]:
    """Return the content blocks from an SDK response object."""
    return list(getattr(response, "content", []) or [])
