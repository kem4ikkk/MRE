#!/usr/bin/env python3
"""Показать предварительную разметку без изменения файлов сервера."""
import hashlib
import importlib.util
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

root = Path(__file__).resolve().parent.parent
spec = importlib.util.spec_from_file_location('draft', root / 'scripts/Prepare-MrePeiZoneDraft.py')
draft = importlib.util.module_from_spec(spec)
spec.loader.exec_module(draft)
chart_path = draft.MAP / 'Chart.png'
if hashlib.sha256(chart_path.read_bytes()).hexdigest() != draft.EXPECTED_CHART:
    raise SystemExit('Изображение PEI изменилось; нужна повторная разметка.')
chart = Image.open(chart_path).convert('RGBA')
if chart.size != (1923, 1927):
    raise SystemExit('Неожиданный размер изображения PEI.')
overlay = Image.new('RGBA', chart.size)
draw = ImageDraw.Draw(overlay)
font = ImageFont.truetype('arial.ttf', 22)
for number, (name, points) in enumerate(draft.PIXELS.items(), 1):
    polygons = points if isinstance(points[0], list) else [points]
    for polygon in polygons:
        draw.polygon(polygon, fill=(240, 46, 39, 73), outline=(255, 50, 40, 235), width=4)
        x, y = polygon[0]
        draw.rounded_rectangle((x+4, y+4, x+43, y+35), radius=5, fill=(40, 18, 18, 225))
        draw.text((x+9, y+5), str(number), font=font, fill='white')
chart = Image.alpha_composite(chart, overlay).convert('RGB')
result = Image.new('RGB', (2423, 1927), '#172328')
result.paste(chart, (0, 0))
draw = ImageDraw.Draw(result)
title = ImageFont.truetype('arialbd.ttf', 27)
draw.text((1943, 28), 'ЗОНЫ PEI — ЧЕРНОВИК', font=title, fill='#ffffff')
draw.text((1943, 76), 'Красным: ускоренное гниение', font=font, fill='#ffd9ce')
draw.text((1943, 108), 'Границы не проверены в игре', font=font, fill='#ffd9ce')
for number, name in enumerate(draft.PIXELS, 1):
    draw.text((1943, 160 + (number-1)*42), f'{number:02d}. {name}', font=font, fill='#ffffff')
output = root / 'planning/pei-decay-zones-draft.png'
result.save(output, optimize=True)
print(output)
