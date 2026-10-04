"""Image preparation for the Claude vision request.

Core is standard-library only (``base64``, ``pathlib``, ``mimetypes``). The
e-kozmu base map arrives as a PDF; its first page must be rendered to a raster
image before it can be sent as a vision block. PDF rendering needs an optional
converter, so this module:

* passes through inputs that are already images (png/jpg/gif/webp);
* renders the first page of a PDF using ``pymupdf`` (fitz) or ``pdf2image`` if
  either is importable;
* raises a clear, actionable error when only a PDF is given and no converter is
  installed, telling the user to install one or pre-render the PDF.

The returned :class:`ImageBlock` carries the base64 data and media type and
knows how to produce an Anthropic Messages API image content block.
"""

from __future__ import annotations

import base64
import importlib.util
from dataclasses import dataclass
from pathlib import Path


# Anthropic vision accepts these raster media types.
_IMAGE_MEDIA_TYPES = {
    ".png": "image/png",
    ".jpg": "image/jpeg",
    ".jpeg": "image/jpeg",
    ".gif": "image/gif",
    ".webp": "image/webp",
}


class PdfConversionError(RuntimeError):
    """Raised when a PDF cannot be converted to an image for the request."""


@dataclass(frozen=True)
class ImageBlock:
    """A base64-encoded image ready to embed as a vision content block."""

    data_base64: str
    media_type: str
    source_name: str

    def to_content_block(self) -> dict:
        """Return the Anthropic Messages API image content block."""
        return {
            "type": "image",
            "source": {
                "type": "base64",
                "media_type": self.media_type,
                "data": self.data_base64,
            },
        }


def _encode_bytes(data: bytes, media_type: str, source_name: str) -> ImageBlock:
    return ImageBlock(
        data_base64=base64.b64encode(data).decode("ascii"),
        media_type=media_type,
        source_name=source_name,
    )


def _is_available(module_name: str) -> bool:
    return importlib.util.find_spec(module_name) is not None


def prepare_image(path: str, dpi: int = 200) -> ImageBlock:
    """Prepare an :class:`ImageBlock` from an image or PDF file path.

    Args:
        path: Path to a png/jpg/gif/webp image or a PDF.
        dpi: Render resolution used when rasterizing a PDF page.

    Raises:
        FileNotFoundError: if the path does not exist.
        PdfConversionError: if the input is a PDF and no converter is available.
        ValueError: if the input is an unsupported file type.
    """
    file_path = Path(path)
    if not file_path.is_file():
        raise FileNotFoundError(f"Input file not found: {path}")

    suffix = file_path.suffix.lower()

    if suffix in _IMAGE_MEDIA_TYPES:
        return _encode_bytes(
            file_path.read_bytes(), _IMAGE_MEDIA_TYPES[suffix], file_path.name
        )

    if suffix == ".pdf":
        return _pdf_first_page_to_image(file_path, dpi)

    raise ValueError(
        f"Unsupported input file type '{suffix}' for {path}. Provide a PDF or a "
        "png/jpg/gif/webp image."
    )


def _pdf_first_page_to_image(file_path: Path, dpi: int) -> ImageBlock:
    """Render the first page of a PDF to a PNG :class:`ImageBlock`."""
    if _is_available("fitz"):
        return _render_with_pymupdf(file_path, dpi)
    if _is_available("pdf2image"):
        return _render_with_pdf2image(file_path, dpi)
    raise PdfConversionError(
        f"Cannot convert PDF '{file_path.name}' to an image: no PDF converter is "
        "installed. Either install an optional converter on your machine "
        "(`pip install pymupdf` or `pip install pdf2image` plus the poppler "
        "system package), or pre-render the base map to a PNG/JPG and pass that "
        "image instead."
    )


def _render_with_pymupdf(file_path: Path, dpi: int) -> ImageBlock:  # pragma: no cover - optional dep
    import fitz  # type: ignore

    document = fitz.open(str(file_path))
    try:
        if document.page_count < 1:
            raise PdfConversionError(f"PDF '{file_path.name}' has no pages.")
        page = document.load_page(0)
        pixmap = page.get_pixmap(dpi=dpi)
        png_bytes = pixmap.tobytes("png")
    finally:
        document.close()
    return _encode_bytes(png_bytes, "image/png", file_path.name)


def _render_with_pdf2image(file_path: Path, dpi: int) -> ImageBlock:  # pragma: no cover - optional dep
    import io

    from pdf2image import convert_from_path  # type: ignore

    pages = convert_from_path(
        str(file_path), dpi=dpi, first_page=1, last_page=1
    )
    if not pages:
        raise PdfConversionError(f"PDF '{file_path.name}' has no pages.")
    buffer = io.BytesIO()
    pages[0].save(buffer, format="PNG")
    return _encode_bytes(buffer.getvalue(), "image/png", file_path.name)
