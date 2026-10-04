"""Command-line interface for the drawing-spec service.

Usage examples::

    # Offline (works in this sandbox, no network, no API key):
    python -m drawingspec_ai --offline --out spec.json

    # Live (on the user's machine, needs ANTHROPIC_API_KEY):
    python -m drawingspec_ai \
        --base-map ekozmu.pdf --sketch sketch.png \
        --brief '6050 Lajosmizse, 0125/2 hrsz. villamos ellatas' \
        --out spec.json

The ``--brief`` value may be literal text or ``@path`` to read the brief from a
file. Offline/dry-run mode produces and validates the bundled sample without any
network access.
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path
from typing import List, Optional

from .config import Config, MissingApiKeyError
from .pipeline import SpecValidationError, generate_spec


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="drawingspec_ai",
        description=(
            "Generate a helyszinrajz drawing-spec JSON from an e-kozmu base map, "
            "an on-site field sketch, and a written brief using Anthropic Claude "
            "vision. Validates against the shared schema before writing."
        ),
    )
    parser.add_argument(
        "--base-map",
        dest="base_map",
        help="Path to the e-kozmu base map PDF or image. Required for live runs.",
    )
    parser.add_argument(
        "--sketch",
        dest="sketch",
        help="Path to the on-site field sketch image (optional).",
    )
    parser.add_argument(
        "--brief",
        dest="brief",
        default="",
        help="Written task brief as literal text, or @path to read from a file.",
    )
    parser.add_argument(
        "--out",
        dest="out",
        required=True,
        help="Output path for the validated drawing-spec JSON.",
    )
    parser.add_argument(
        "--config",
        dest="config",
        help="Optional JSON config file (api_key, model, max_tokens, base_url).",
    )
    parser.add_argument(
        "--model",
        dest="model",
        help="Override the Claude model name (vision capable).",
    )
    parser.add_argument(
        "--offline",
        "--dry-run",
        dest="offline",
        action="store_true",
        help=(
            "Skip the live API call and use the bundled sample spec. Runs the "
            "full build -> parse -> validate -> write path with no network."
        ),
    )
    return parser


def _read_brief(brief_arg: str) -> str:
    """Return the brief text, resolving a leading ``@`` as a file reference."""
    if brief_arg.startswith("@"):
        brief_path = Path(brief_arg[1:])
        if not brief_path.is_file():
            raise FileNotFoundError(f"Brief file not found: {brief_path}")
        return brief_path.read_text(encoding="utf-8")
    return brief_arg


def main(argv: Optional[List[str]] = None) -> int:
    """CLI entry point. Returns a process exit code."""
    parser = build_parser()
    args = parser.parse_args(argv)

    try:
        brief_text = _read_brief(args.brief)
    except FileNotFoundError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2

    overrides = {"model": args.model} if args.model else None
    config = Config.from_env(config_file=args.config, overrides=overrides)

    if not args.offline and not args.base_map:
        print(
            "error: --base-map is required for a live run. Use --offline to run "
            "with the bundled sample instead.",
            file=sys.stderr,
        )
        return 2

    try:
        generate_spec(
            base_map_path=args.base_map,
            sketch_path=args.sketch,
            brief_text=brief_text,
            config=config,
            offline=args.offline,
            out_path=args.out,
        )
    except MissingApiKeyError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2
    except SpecValidationError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 1
    except (FileNotFoundError, ValueError, RuntimeError) as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 1

    mode = "offline" if args.offline else "live"
    print(f"Wrote validated drawing spec to {args.out} ({mode} mode).")
    return 0


if __name__ == "__main__":  # pragma: no cover
    sys.exit(main())
