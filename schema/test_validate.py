"""Standard-library unittest suite for the drawing-spec validator.

Asserts that the shipped Lajosmizse sample validates clean against the shipped
schema and that representative mutations fail, including the georeference
exactly-2 constraint and additionalProperties strictness.
"""

from __future__ import annotations

import copy
import json
import pathlib
import unittest

from validate import validate_spec


HERE = pathlib.Path(__file__).resolve().parent
SCHEMA_PATH = HERE / "drawing-spec.schema.json"
SAMPLE_PATH = HERE / "samples" / "sample-lajosmizse.drawingspec.json"


def _load(path: pathlib.Path) -> dict:
    with path.open("r", encoding="utf-8") as handle:
        return json.load(handle)


class ValidatorTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.schema = _load(SCHEMA_PATH)
        cls.sample = _load(SAMPLE_PATH)

    def _fresh_sample(self) -> dict:
        return copy.deepcopy(self.sample)

    def test_sample_validates_clean(self) -> None:
        errors = validate_spec(self.sample, self.schema)
        self.assertEqual(errors, [], f"sample should be valid, got: {errors}")

    def test_missing_required_rajzszam_fails(self) -> None:
        spec = self._fresh_sample()
        del spec["project"]["rajzszam"]
        errors = validate_spec(spec, self.schema)
        self.assertTrue(errors, "missing rajzszam should fail")
        self.assertTrue(any("rajzszam" in e for e in errors), errors)

    def test_georeference_one_point_fails(self) -> None:
        spec = self._fresh_sample()
        spec["georeference"] = [spec["georeference"][0]]
        errors = validate_spec(spec, self.schema)
        self.assertTrue(errors, "georeference with 1 point should fail minItems")
        self.assertTrue(any("georeference" in e for e in errors), errors)

    def test_georeference_three_points_fails(self) -> None:
        spec = self._fresh_sample()
        spec["georeference"] = spec["georeference"] + [
            copy.deepcopy(spec["georeference"][0])
        ]
        errors = validate_spec(spec, self.schema)
        self.assertTrue(errors, "georeference with 3 points should fail maxItems")
        self.assertTrue(any("georeference" in e for e in errors), errors)

    def test_vertex_wrong_type_fails(self) -> None:
        spec = self._fresh_sample()
        spec["cable_runs"][0]["vertices"][0]["x"] = "not-a-number"
        errors = validate_spec(spec, self.schema)
        self.assertTrue(errors, "non-numeric vertex coordinate should fail")
        self.assertTrue(any("vertices" in e for e in errors), errors)

    def test_unknown_additional_property_fails(self) -> None:
        spec = self._fresh_sample()
        spec["project"]["unexpected_field"] = "x"
        errors = validate_spec(spec, self.schema)
        self.assertTrue(errors, "unknown property should fail additionalProperties")
        self.assertTrue(
            any("unexpected_field" in e for e in errors), errors
        )

    def test_scale_denominator_const(self) -> None:
        spec = self._fresh_sample()
        spec["project"]["scale_denominator"] = 1000
        errors = validate_spec(spec, self.schema)
        self.assertTrue(errors, "scale_denominator must be const 500")

    def test_sample_contains_exact_strings(self) -> None:
        raw = SAMPLE_PATH.read_text(encoding="utf-8")
        for needle in (
            "6050 Lajosmizse, 0125/2 hrsz. alatti ingatlan villamos energia ellátása",
            "ALL-26/10-01/1",
            "ALL IN VILL KFT.",
            "4x35mm2 NAYY-J",
            "S300 tip. MN 1x32A",
        ):
            self.assertIn(needle, raw, f"sample must contain exact string: {needle}")
        self.assertEqual(self.sample["project"]["scale_denominator"], 500)

    def test_all_legend_keys_present(self) -> None:
        expected_keys = {
            "tervezett_fogy_mero_szekreny",
            "tervezett_csatlakozo_foldkabel",
            "tervezett_kutatoarok",
            "meglevo_04kv_halozat",
            "meglevo_20kv_halozat",
            "meglevo_gazvezetek",
            "meglevo_vizvezetek",
            "meglevo_szennyvizvezetek",
            "meglevo_foldfeletti_hirkozlesi",
            "meglevo_foldalatti_hirkozlesi",
        }
        legend_keys = {entry["symbol_key"] for entry in self.sample["legend"]}
        self.assertEqual(legend_keys, expected_keys)

        symbols_config = _load(HERE / "config" / "symbols.config.json")
        config_keys = set(symbols_config["symbols"].keys())
        self.assertTrue(
            expected_keys.issubset(config_keys),
            f"symbols.config.json missing keys: {expected_keys - config_keys}",
        )


if __name__ == "__main__":
    unittest.main()
