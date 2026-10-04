"""Standard-library unittest suite for the drawingspec_ai package.

These tests must pass with no network access and without any third-party
package installed. Any potential network call is replaced with a mock.
"""

from __future__ import annotations

import importlib.util
import json
import os
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

# Ensure the package is importable when running from the repo root or elsewhere.
_THIS_DIR = Path(__file__).resolve().parent
if str(_THIS_DIR) not in sys.path:
    sys.path.insert(0, str(_THIS_DIR))

from drawingspec_ai import (  # noqa: E402
    Config,
    MissingApiKeyError,
    OfflineClaudeClient,
    UrllibClaudeClient,
    build_client,
    extract_json_object,
    generate_spec,
)
from drawingspec_ai import clients as clients_module  # noqa: E402
from drawingspec_ai.pipeline import SpecValidationError, validate_spec_dict  # noqa: E402


# Load the shared schema validator by path for cross-checks.
_REPO_ROOT = _THIS_DIR.parent
_SCHEMA_DIR = _REPO_ROOT / "schema"


def _load_validate_spec():
    spec = importlib.util.spec_from_file_location(
        "schema_validate_test", str(_SCHEMA_DIR / "validate.py")
    )
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module.validate_spec


class OfflineModeTests(unittest.TestCase):
    def test_offline_generates_schema_valid_dict(self) -> None:
        config = Config.from_env()
        spec = generate_spec(
            base_map_path=None,
            sketch_path=None,
            brief_text="test brief",
            config=config,
            offline=True,
        )
        self.assertIsInstance(spec, dict)
        validate_spec = _load_validate_spec()
        schema = json.loads(
            (_SCHEMA_DIR / "drawing-spec.schema.json").read_text(encoding="utf-8")
        )
        self.assertEqual(validate_spec(spec, schema), [])

    def test_offline_writes_file(self) -> None:
        config = Config.from_env()
        with tempfile.TemporaryDirectory() as tmp:
            out = Path(tmp) / "spec.json"
            generate_spec(
                base_map_path=None,
                sketch_path=None,
                brief_text="test brief",
                config=config,
                offline=True,
                out_path=str(out),
            )
            self.assertTrue(out.is_file())
            written = json.loads(out.read_text(encoding="utf-8"))
            self.assertEqual(written["meta"]["schema_version"], "1.0.0")

    def test_offline_needs_no_api_key(self) -> None:
        with mock.patch.dict(os.environ, {}, clear=True):
            config = Config.from_env()
            self.assertIsNone(config.api_key)
            # Must not raise despite the missing key.
            spec = generate_spec(
                base_map_path=None,
                sketch_path=None,
                brief_text="",
                config=config,
                offline=True,
            )
            self.assertIn("meta", spec)


class JsonExtractionTests(unittest.TestCase):
    def test_strips_json_code_fence_and_prose(self) -> None:
        text = (
            "Here is the drawing spec you requested:\n\n"
            "```json\n"
            '{"meta": {"schema_version": "1.0.0"}, "n": 1}\n'
            "```\n\n"
            "Let me know if you need changes."
        )
        result = extract_json_object(text)
        self.assertEqual(result["n"], 1)
        self.assertEqual(result["meta"]["schema_version"], "1.0.0")

    def test_plain_json_passthrough(self) -> None:
        result = extract_json_object('{"a": 2}')
        self.assertEqual(result, {"a": 2})

    def test_prose_without_fence(self) -> None:
        text = 'Sure! {"a": 1, "b": [1,2]} Done.'
        result = extract_json_object(text)
        self.assertEqual(result, {"a": 1, "b": [1, 2]})

    def test_bare_fence_without_language_tag(self) -> None:
        text = "```\n{\"x\": 10}\n```"
        self.assertEqual(extract_json_object(text), {"x": 10})

    def test_empty_text_raises(self) -> None:
        with self.assertRaises(ValueError):
            extract_json_object("   ")

    def test_no_object_raises(self) -> None:
        with self.assertRaises(ValueError):
            extract_json_object("no json here at all")


class BuildClientTests(unittest.TestCase):
    def test_offline_returns_offline_client(self) -> None:
        config = Config.from_env()
        client = build_client(config, offline=True)
        self.assertIsInstance(client, OfflineClaudeClient)

    def test_live_without_sdk_returns_urllib_client(self) -> None:
        config = Config(api_key="sk-ant-test")
        with mock.patch.object(
            clients_module, "is_anthropic_sdk_available", return_value=False
        ):
            client = build_client(config, offline=False)
        self.assertIsInstance(client, UrllibClaudeClient)

    def test_live_with_sdk_builds_sdk_client(self) -> None:
        # Simulate the SDK being available but avoid importing a real package:
        # SdkClaudeClient.__init__ is patched to a no-op so no network/import.
        config = Config(api_key="sk-ant-test")
        sentinel = object()
        with mock.patch.object(
            clients_module, "is_anthropic_sdk_available", return_value=True
        ), mock.patch.object(
            clients_module, "SdkClaudeClient", return_value=sentinel
        ) as sdk_ctor:
            client = build_client(config, offline=False)
        self.assertIs(client, sentinel)
        sdk_ctor.assert_called_once_with(config)


class ConfigTests(unittest.TestCase):
    def test_reads_api_key_from_env(self) -> None:
        with mock.patch.dict(
            os.environ, {"ANTHROPIC_API_KEY": "sk-ant-env"}, clear=True
        ):
            config = Config.from_env()
            self.assertEqual(config.api_key, "sk-ant-env")

    def test_env_model_and_overrides(self) -> None:
        with mock.patch.dict(
            os.environ,
            {"ANTHROPIC_API_KEY": "k", "ANTHROPIC_MODEL": "env-model"},
            clear=True,
        ):
            config = Config.from_env(overrides={"model": "override-model"})
            self.assertEqual(config.model, "override-model")

    def test_key_never_in_repr_or_redacted(self) -> None:
        config = Config(api_key="sk-ant-super-secret")
        self.assertNotIn("super-secret", repr(config))
        self.assertNotIn("super-secret", json.dumps(config.redacted_dict()))
        self.assertEqual(config.redacted_dict()["api_key"], "<set>")

    def test_missing_key_raises_only_on_live(self) -> None:
        with mock.patch.dict(os.environ, {}, clear=True):
            config = Config.from_env()
            # Offline path never touches require_api_key -> no raise.
            spec = generate_spec(
                base_map_path=None,
                sketch_path=None,
                brief_text="",
                config=config,
                offline=True,
            )
            self.assertIn("meta", spec)
            # Live path (building the urllib client) must raise clearly.
            with self.assertRaises(MissingApiKeyError):
                UrllibClaudeClient(config)

    def test_require_api_key_message_is_actionable(self) -> None:
        config = Config(api_key=None)
        with self.assertRaises(MissingApiKeyError) as ctx:
            config.require_api_key()
        message = str(ctx.exception)
        self.assertIn("console.anthropic.com", message)
        self.assertIn("Max", message)


class UrllibClientTests(unittest.TestCase):
    def test_send_messages_parses_text_blocks_without_network(self) -> None:
        config = Config(api_key="sk-ant-test")
        client = UrllibClaudeClient(config)

        fake_payload = json.dumps(
            {"content": [{"type": "text", "text": '{"ok": true}'}]}
        ).encode("utf-8")

        class _FakeResponse:
            def __enter__(self_inner):
                return self_inner

            def __exit__(self_inner, *args):
                return False

            def read(self_inner):
                return fake_payload

        with mock.patch(
            "drawingspec_ai.clients.urllib.request.urlopen",
            return_value=_FakeResponse(),
        ) as urlopen:
            text = client.send_messages("sys", [{"role": "user", "content": []}])

        self.assertEqual(text, '{"ok": true}')
        urlopen.assert_called_once()


class ValidationErrorTests(unittest.TestCase):
    def test_invalid_spec_raises_with_errors(self) -> None:
        # An offline client returning an invalid spec must raise rather than
        # write. Patch the offline backend to return an incomplete object.
        config = Config.from_env()
        bad = clients_module.OfflineClaudeClient(canned_text='{"meta": {}}')
        with mock.patch(
            "drawingspec_ai.pipeline.build_client", return_value=bad
        ):
            with self.assertRaises(SpecValidationError) as ctx:
                generate_spec(
                    base_map_path=None,
                    sketch_path=None,
                    brief_text="",
                    config=config,
                    offline=True,
                )
        self.assertTrue(ctx.exception.errors)

    def test_validate_spec_dict_on_sample(self) -> None:
        sample = json.loads(
            (_SCHEMA_DIR / "samples" / "sample-lajosmizse.drawingspec.json").read_text(
                encoding="utf-8"
            )
        )
        self.assertEqual(validate_spec_dict(sample), [])


if __name__ == "__main__":
    unittest.main()
