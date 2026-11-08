"""Original white corner masks for Unity nine-slice UI; requires Pillow."""
from pathlib import Path
from PIL import Image, ImageDraw

for radius in (8, 12, 16, 20):
    image = Image.new("RGBA", (256, 256))
    ImageDraw.Draw(image).rounded_rectangle(
        (0, 0, 255, 255), radius=radius * 4, fill=(255, 255, 255, 255)
    )
    image.resize((64, 64), Image.Resampling.LANCZOS).save(
        Path(__file__).parent / f"round-{radius}.png"
    )
