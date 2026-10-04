"""Prompt construction for the helyszinrajz drawing-spec generation.

Builds the system prompt (role, rules, required output shape, symbol_key enum)
and the user message (text brief plus base64 image vision blocks).

The model is instructed to output ONLY a single JSON object conforming to
``schema/drawing-spec.schema.json``; the pipeline still strips any stray prose
or code fences defensively.
"""

from __future__ import annotations

from typing import List, Optional

from .pdf_utils import ImageBlock


# The 10 symbol keys defined in schema/config/symbols.config.json. The prompt
# enumerates them so the model only emits known keys. Kept in sync with that
# config file (do not edit the schema/ dir).
SYMBOL_KEYS: List[str] = [
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
]


def build_system_prompt() -> str:
    """Return the system prompt instructing Claude how to produce the spec."""
    symbol_list = "\n".join(f"  - {key}" for key in SYMBOL_KEYS)
    return (
        "You are a Hungarian electrical site-plan (helyszinrajz) drafting "
        "assistant. You help prepare a villamos csatlakozasi helyszinrajz from "
        "an e-kozmu (e-utility) base map and an on-site field sketch.\n"
        "\n"
        "Inputs you receive:\n"
        "  1. A raster image of the e-kozmu base map (first page of the PDF). "
        "It is raster only, no vector geometry, drawn at 1:500.\n"
        "  2. An on-site field sketch image indicating the planned connection "
        "layout (cable route, meter cabinet, exploratory trench).\n"
        "  3. A short written brief with project data.\n"
        "\n"
        "Your task: output ONLY a single JSON object that conforms to the "
        "helyszinrajz drawing-spec schema. Do not output markdown, code fences, "
        "or any prose before or after the JSON. The JSON object must contain "
        "exactly these top-level keys: meta, project, raster, georeference, "
        "layers, symbols, cable_runs, legend, title_block, bom, summary "
        "(labels is optional).\n"
        "\n"
        "Fixed drawing rules (do not deviate):\n"
        "  - Scale is fixed at 1:500. Set project.scale_denominator = 500, "
        "raster.attach.scale_denominator = 500, and summary.meretarany = "
        "'1:500'.\n"
        "  - georeference MUST contain EXACTLY 2 reference points. Each has "
        "image {x,y}, model {x,y}, and units = 'meter'. Two points fix "
        "translation, rotation, and uniform scale.\n"
        "  - 1 drawing unit = 1 meter. All model coordinates, vertices, and "
        "lengths are in meters.\n"
        "  - meta.schema_version MUST be '1.0.0'.\n"
        "  - meta.confidence_note MUST state that this is a draft proposal and "
        "that a human engineer confirms survey-accurate placement before issue. "
        "You never guarantee survey accuracy; the human engineer does.\n"
        "\n"
        "symbols[].symbol_key, cable_runs[].symbol_key, and legend[].symbol_key "
        "MUST be chosen from exactly this set of keys:\n"
        f"{symbol_list}\n"
        "\n"
        "Preserve Hungarian domain terms verbatim (jelmagyarazat, "
        "fogyasztasmero szekreny, csatlakozo foldkabel, kutatoarok, meglevo "
        "halozat, etc.). Populate legend (jelmagyarazat), title_block "
        "(rajzpecset), bom (anyagjegyzek), and summary (osszegzes) consistently "
        "with the symbols and cable runs you place."
    )


def build_user_message(
    brief_text: str,
    base_map_image: ImageBlock,
    sketch_image: Optional[ImageBlock],
) -> dict:
    """Build the Anthropic ``user`` message with text + base64 image blocks."""
    content: List[dict] = []

    intro = (
        "Keszitsd el a helyszinrajz drawing-spec JSON-t az alabbi leiras es a "
        "csatolt kepek alapjan.\n\n"
        "Written brief / Leiras:\n"
        f"{brief_text.strip()}\n"
    )
    content.append({"type": "text", "text": intro})

    content.append({"type": "text", "text": "E-kozmu alaterkep (base map, 1:500):"})
    content.append(base_map_image.to_content_block())

    if sketch_image is not None:
        content.append({"type": "text", "text": "Helyszini vazlat (field sketch):"})
        content.append(sketch_image.to_content_block())

    content.append(
        {
            "type": "text",
            "text": (
                "Output ONLY the JSON object conforming to the drawing-spec "
                "schema. No prose, no code fences."
            ),
        }
    )

    return {"role": "user", "content": content}


def build_messages(
    brief_text: str,
    base_map_image: ImageBlock,
    sketch_image: Optional[ImageBlock],
) -> List[dict]:
    """Convenience wrapper returning the full messages list."""
    return [build_user_message(brief_text, base_map_image, sketch_image)]
