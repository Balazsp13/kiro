"""Cross-check that prompt.SYMBOL_KEYS matches schema/config/symbols.config.json.

prompt.SYMBOL_KEYS and schema/config/symbols.config.json are a hand-maintained,
duplicated source of truth for the symbol vocabulary. They agree today, but a
future edit to one and not the other would silently drift and let the AI emit
keys the plugin's symbol catalog does not know (or vice versa). This test loads
the config and asserts the two agree as a set, so any such drift fails here.

Pure standard-library unittest: no network, nothing pip-installed.
"""

from __future__ import annotations

import json
import sys
import unittest
from pathlib import Path

# Make the package importable when running from the repo root or ai-service/.
_THIS_DIR = Path(__file__).resolve().parent
if str(_THIS_DIR) not in sys.path:
    sys.path.insert(0, str(_THIS_DIR))

from drawingspec_ai import prompt as prompt_module  # noqa: E402

_REPO_ROOT = _THIS_DIR.parent
_SYMBOLS_CONFIG = _REPO_ROOT / "schema" / "config" / "symbols.config.json"


def _load_config_symbol_keys() -> set:
    with _SYMBOLS_CONFIG.open(encoding="utf-8") as fh:
        config = json.load(fh)
    return set(config["symbols"].keys())


class SymbolKeyCrossCheckTests(unittest.TestCase):
    def test_config_file_exists(self) -> None:
        self.assertTrue(
            _SYMBOLS_CONFIG.is_file(),
            f"symbols.config.json not found at {_SYMBOLS_CONFIG}",
        )

    def test_prompt_keys_equal_config_keys(self) -> None:
        config_keys = _load_config_symbol_keys()
        prompt_keys = set(prompt_module.SYMBOL_KEYS)

        missing_in_prompt = config_keys - prompt_keys
        extra_in_prompt = prompt_keys - config_keys

        self.assertEqual(
            config_keys,
            prompt_keys,
            "prompt.SYMBOL_KEYS and symbols.config.json drifted. "
            f"In config but not in prompt: {sorted(missing_in_prompt)}; "
            f"in prompt but not in config: {sorted(extra_in_prompt)}.",
        )

    def test_prompt_keys_have_no_duplicates(self) -> None:
        keys = prompt_module.SYMBOL_KEYS
        self.assertEqual(
            len(keys),
            len(set(keys)),
            "prompt.SYMBOL_KEYS contains duplicate entries.",
        )


if __name__ == "__main__":
    unittest.main()
