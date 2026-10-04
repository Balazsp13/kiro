"""Configuration for the Claude-backed drawing-spec service.

The :class:`Config` dataclass is loaded from environment variables and/or an
optional JSON config file. It carries the Anthropic API key, the model name,
the token budget, and the API base URL.

Security note: the API key is NEVER logged or included in ``repr``. Callers that
need to serialize config for debugging get a redacted view via
:meth:`Config.redacted_dict`.

API key vs. subscription note: a claude.ai "Max" (or "Pro") subscription is a
chat UI plan and is NOT an API key. To call the Anthropic API programmatically
you must create an API key at https://console.anthropic.com (Settings -> API
Keys) and set it in the ANTHROPIC_API_KEY environment variable. Billing for API
usage is separate from any claude.ai subscription.
"""

from __future__ import annotations

import json
import os
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Optional


# Default vision-capable Claude model. Override with ANTHROPIC_MODEL or --model.
# Claude 3.7 Sonnet is a current vision-capable model at the time of writing;
# the user can point this at any newer vision model their account can access.
DEFAULT_MODEL = "claude-3-7-sonnet-20250219"
DEFAULT_MAX_TOKENS = 8192
DEFAULT_BASE_URL = "https://api.anthropic.com"
# Anthropic Messages API version header value.
ANTHROPIC_VERSION = "2023-06-01"


class MissingApiKeyError(RuntimeError):
    """Raised when a live Claude call is attempted without an API key.

    The message is actionable and must never embed any secret value.
    """


@dataclass
class Config:
    """Runtime configuration for the drawing-spec service.

    Attributes:
        api_key: Anthropic API key. May be ``None`` in offline/dry-run mode.
        model: Model name (vision capable). Defaults to :data:`DEFAULT_MODEL`.
        max_tokens: Maximum tokens for the model response.
        base_url: Anthropic API base URL.
        anthropic_version: Value of the ``anthropic-version`` header.
    """

    api_key: Optional[str] = field(default=None, repr=False)
    model: str = DEFAULT_MODEL
    max_tokens: int = DEFAULT_MAX_TOKENS
    base_url: str = DEFAULT_BASE_URL
    anthropic_version: str = ANTHROPIC_VERSION

    def __repr__(self) -> str:  # pragma: no cover - trivial, but keep key hidden
        present = "set" if self.api_key else "unset"
        return (
            f"Config(api_key=<{present}>, model={self.model!r}, "
            f"max_tokens={self.max_tokens}, base_url={self.base_url!r}, "
            f"anthropic_version={self.anthropic_version!r})"
        )

    @classmethod
    def from_env(
        cls,
        config_file: Optional[str] = None,
        overrides: Optional[dict] = None,
    ) -> "Config":
        """Build a Config from (lowest to highest priority): defaults, config
        file, environment variables, then explicit ``overrides``.

        The config file is JSON with any of the keys: ``api_key``, ``model``,
        ``max_tokens``, ``base_url``. Environment variables ANTHROPIC_API_KEY,
        ANTHROPIC_MODEL, ANTHROPIC_MAX_TOKENS, ANTHROPIC_BASE_URL override it.
        """
        values: dict[str, Any] = {}

        if config_file:
            file_data = _load_config_file(config_file)
            for key in ("api_key", "model", "max_tokens", "base_url"):
                if key in file_data and file_data[key] is not None:
                    values[key] = file_data[key]

        env_api_key = os.environ.get("ANTHROPIC_API_KEY")
        if env_api_key:
            values["api_key"] = env_api_key
        env_model = os.environ.get("ANTHROPIC_MODEL")
        if env_model:
            values["model"] = env_model
        env_max_tokens = os.environ.get("ANTHROPIC_MAX_TOKENS")
        if env_max_tokens:
            values["max_tokens"] = int(env_max_tokens)
        env_base_url = os.environ.get("ANTHROPIC_BASE_URL")
        if env_base_url:
            values["base_url"] = env_base_url

        if overrides:
            for key, value in overrides.items():
                if value is not None:
                    values[key] = value

        return cls(**values)

    def require_api_key(self) -> str:
        """Return the API key or raise an actionable error.

        Only call this on a live (non-offline) code path. Offline mode must
        never invoke this so that it works with no key configured.
        """
        if not self.api_key:
            raise MissingApiKeyError(
                "No Anthropic API key configured. Set the ANTHROPIC_API_KEY "
                "environment variable (or 'api_key' in the --config file) to a "
                "key created at https://console.anthropic.com. Note: a claude.ai "
                "'Max' or 'Pro' subscription is NOT an API key. Alternatively run "
                "with --offline/--dry-run to use the bundled sample without any "
                "network access."
            )
        return self.api_key

    def redacted_dict(self) -> dict:
        """Return a dict view of the config safe to log (key redacted)."""
        return {
            "api_key": "<set>" if self.api_key else "<unset>",
            "model": self.model,
            "max_tokens": self.max_tokens,
            "base_url": self.base_url,
            "anthropic_version": self.anthropic_version,
        }


def _load_config_file(config_file: str) -> dict:
    path = Path(config_file)
    if not path.is_file():
        raise FileNotFoundError(f"Config file not found: {config_file}")
    with path.open("r", encoding="utf-8") as handle:
        data = json.load(handle)
    if not isinstance(data, dict):
        raise ValueError(f"Config file {config_file} must contain a JSON object")
    return data
