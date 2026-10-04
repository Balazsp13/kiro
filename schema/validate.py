"""Pure standard-library JSON Schema validator for the helyszinrajz drawing-spec.

This module validates a drawing-spec document against drawing-spec.schema.json.
It supports the subset of JSON Schema draft 2020-12 used by that schema:

    type, required, properties, additionalProperties, items, prefixItems,
    enum, const, minItems, maxItems, minimum, maximum, $ref (to local $defs),
    oneOf, anyOf.

The sandbox has no PyPI access, so there is no hard dependency on the
third-party ``jsonschema`` package. If that package happens to be importable
it is preferred; otherwise the built-in validator below is used.

Usage as a library::

    from validate import validate_spec
    errors = validate_spec(spec_dict, schema_dict)  # [] means valid

Usage as a CLI::

    python validate.py <spec.json> <schema.json>

Prints ``VALID`` and exits 0 when the spec is valid; otherwise prints the
error list and exits 1.
"""

from __future__ import annotations

import json
import sys
from typing import Any


JSONType = Any


def _type_matches(value: JSONType, expected: str) -> bool:
    """Return True if ``value`` matches the JSON Schema ``expected`` type."""
    if expected == "object":
        return isinstance(value, dict)
    if expected == "array":
        return isinstance(value, list)
    if expected == "string":
        return isinstance(value, str)
    if expected == "integer":
        # bool is a subclass of int in Python; exclude it.
        return isinstance(value, int) and not isinstance(value, bool)
    if expected == "number":
        return (isinstance(value, (int, float)) and not isinstance(value, bool))
    if expected == "boolean":
        return isinstance(value, bool)
    if expected == "null":
        return value is None
    # Unknown type keyword: do not fail validation on it.
    return True


def _resolve_ref(ref: str, root: dict) -> dict:
    """Resolve a local ``$ref`` (JSON pointer starting with ``#/``)."""
    if not ref.startswith("#/"):
        raise ValueError(f"Only local refs are supported, got: {ref}")
    node: JSONType = root
    for raw_token in ref[2:].split("/"):
        token = raw_token.replace("~1", "/").replace("~0", "~")
        if not isinstance(node, dict) or token not in node:
            raise ValueError(f"Cannot resolve ref: {ref}")
        node = node[token]
    if not isinstance(node, dict):
        raise ValueError(f"Ref does not point to a schema object: {ref}")
    return node


class _Validator:
    """Recursive validator collecting human-readable error paths."""

    def __init__(self, root_schema: dict) -> None:
        self.root = root_schema
        self.errors: list[str] = []

    def validate(self, instance: JSONType, schema: dict, path: str) -> None:
        # Resolve $ref first; a $ref schema may still carry sibling keywords,
        # but drawing-spec.schema.json uses $ref standalone, so merge is simple.
        if "$ref" in schema:
            resolved = _resolve_ref(schema["$ref"], self.root)
            self.validate(instance, resolved, path)
            # Validate any sibling keywords alongside the ref as well.
            siblings = {k: v for k, v in schema.items() if k != "$ref"}
            if siblings:
                self.validate(instance, siblings, path)
            return

        self._check_type(instance, schema, path)
        self._check_const_enum(instance, schema, path)
        self._check_numeric(instance, schema, path)
        self._check_combinators(instance, schema, path)

        if isinstance(instance, dict):
            self._check_object(instance, schema, path)
        if isinstance(instance, list):
            self._check_array(instance, schema, path)

    def _check_type(self, instance: JSONType, schema: dict, path: str) -> None:
        if "type" not in schema:
            return
        expected = schema["type"]
        types = expected if isinstance(expected, list) else [expected]
        if not any(_type_matches(instance, t) for t in types):
            self.errors.append(
                f"{path}: expected type {expected}, got {type(instance).__name__}"
            )

    def _check_const_enum(self, instance: JSONType, schema: dict, path: str) -> None:
        if "const" in schema and instance != schema["const"]:
            self.errors.append(
                f"{path}: expected const {schema['const']!r}, got {instance!r}"
            )
        if "enum" in schema and instance not in schema["enum"]:
            self.errors.append(
                f"{path}: value {instance!r} not in enum {schema['enum']!r}"
            )

    def _check_numeric(self, instance: JSONType, schema: dict, path: str) -> None:
        if not (isinstance(instance, (int, float)) and not isinstance(instance, bool)):
            return
        if "minimum" in schema and instance < schema["minimum"]:
            self.errors.append(
                f"{path}: value {instance} is below minimum {schema['minimum']}"
            )
        if "maximum" in schema and instance > schema["maximum"]:
            self.errors.append(
                f"{path}: value {instance} is above maximum {schema['maximum']}"
            )

    def _check_combinators(self, instance: JSONType, schema: dict, path: str) -> None:
        if "oneOf" in schema:
            matches = 0
            for sub in schema["oneOf"]:
                probe = _Validator(self.root)
                probe.validate(instance, sub, path)
                if not probe.errors:
                    matches += 1
            if matches != 1:
                self.errors.append(
                    f"{path}: expected exactly one oneOf branch to match, matched {matches}"
                )
        if "anyOf" in schema:
            if not any(
                not self._probe(instance, sub, path) for sub in schema["anyOf"]
            ):
                self.errors.append(f"{path}: no anyOf branch matched")

    def _probe(self, instance: JSONType, schema: dict, path: str) -> list[str]:
        probe = _Validator(self.root)
        probe.validate(instance, schema, path)
        return probe.errors

    def _check_object(self, instance: dict, schema: dict, path: str) -> None:
        for key in schema.get("required", []):
            if key not in instance:
                self.errors.append(f"{path}: missing required property '{key}'")

        properties = schema.get("properties", {})
        for key, value in instance.items():
            child_path = f"{path}.{key}" if path else key
            if key in properties:
                self.validate(value, properties[key], child_path)
            else:
                additional = schema.get("additionalProperties", True)
                if additional is False:
                    self.errors.append(
                        f"{path}: additional property '{key}' is not allowed"
                    )
                elif isinstance(additional, dict):
                    self.validate(value, additional, child_path)

    def _check_array(self, instance: list, schema: dict, path: str) -> None:
        if "minItems" in schema and len(instance) < schema["minItems"]:
            self.errors.append(
                f"{path}: array has {len(instance)} items, fewer than minItems "
                f"{schema['minItems']}"
            )
        if "maxItems" in schema and len(instance) > schema["maxItems"]:
            self.errors.append(
                f"{path}: array has {len(instance)} items, more than maxItems "
                f"{schema['maxItems']}"
            )

        prefix = schema.get("prefixItems")
        if isinstance(prefix, list):
            for index, sub in enumerate(prefix):
                if index < len(instance):
                    self.validate(instance[index], sub, f"{path}[{index}]")

        items = schema.get("items")
        start = len(prefix) if isinstance(prefix, list) else 0
        if isinstance(items, dict):
            for index in range(start, len(instance)):
                self.validate(instance[index], items, f"{path}[{index}]")
        elif items is False and isinstance(prefix, list):
            for index in range(start, len(instance)):
                self.errors.append(
                    f"{path}[{index}]: additional array item is not allowed"
                )


def validate_spec(spec: dict, schema: dict) -> list[str]:
    """Validate ``spec`` against ``schema``.

    Returns a list of human-readable error strings. An empty list means the
    document is valid. Prefers the third-party ``jsonschema`` package when it
    is importable, otherwise uses the built-in validator.
    """
    try:
        import jsonschema  # type: ignore

        validator_cls = jsonschema.Draft202012Validator  # type: ignore[attr-defined]
        validator = validator_cls(schema)
        errors = sorted(validator.iter_errors(spec), key=lambda e: list(e.path))
        return [
            f"{'/'.join(str(p) for p in err.path) or '<root>'}: {err.message}"
            for err in errors
        ]
    except Exception:
        # jsonschema not installed (expected in the sandbox) or failed to run;
        # fall back to the built-in validator.
        validator = _Validator(schema)
        validator.validate(spec, schema, "")
        return validator.errors


def _load_json(file_path: str) -> dict:
    with open(file_path, "r", encoding="utf-8") as handle:
        data = json.load(handle)
    if not isinstance(data, dict):
        raise ValueError(f"Top-level JSON in {file_path} must be an object")
    return data


def main(argv: list[str]) -> int:
    """CLI entry point. Returns the process exit code."""
    if len(argv) != 3:
        print("usage: python validate.py <spec.json> <schema.json>", file=sys.stderr)
        return 2
    spec = _load_json(argv[1])
    schema = _load_json(argv[2])
    errors = validate_spec(spec, schema)
    if not errors:
        print("VALID")
        return 0
    print("INVALID")
    for error in errors:
        print(f"  - {error}")
    return 1


if __name__ == "__main__":
    sys.exit(main(sys.argv))
