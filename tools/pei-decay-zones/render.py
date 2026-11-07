#!/usr/bin/env python3
"""Собрать PNG-предпросмотр из чернового JSON и локальной карты PEI."""
import hashlib
import json
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[2]
DRAFT = ROOT / 'planning/pei-decay-zones.draft.json'
CHART = ROOT / 'runtime/server/Maps/PEI/Chart.png'
OUTPUT = ROOT / 'planning/pei-decay-zones-draft.png'

doc = json.loads(DRAFT.read_text(encoding='utf-8-sig'))
if doc.get('status') != 'DRAFT_NOT_FOR_AUTOMATIC_APPLICATION':
    raise SystemExit('Ожидался черновик, не предназначенный для автоматического применения.')
chart_bytes = CHART.read_bytes()
if hashlib.sha256(chart_bytes).hexdigest() != doc.get('chart_sha256'):
    raise SystemExit('Хеш Chart.png не совпадает с черновиком; нужна проверка разметки.')
chart = Image.open(CHART).convert('RGBA')
width, height = chart.size
volume = doc['cartography']
position, scale = volume['position'], volume['scale']


def to_pixel(point):
    return (
        ((point['x'] - float(position['x'])) / float(scale['x']) + 0.5) * width,
        (0.5 - (point['z'] - float(position['z'])) / float(scale['z'])) * height,
    )


overlay = Image.new('RGBA', chart.size)
draw = ImageDraw.Draw(overlay)
font = ImageFont.truetype('arial.ttf', 22)
for number, zone in enumerate(doc['zones'], 1):
    for polygon in zone['polygons']:
        points = [to_pixel(point) for point in polygon]
        draw.polygon(points, fill=(240, 46, 39, 73), outline=(255, 50, 40, 235), width=4)
        x, y = points[0]
        draw.rounded_rectangle((x + 4, y + 4, x + 43, y + 35), radius=5, fill=(40, 18, 18, 225))
        draw.text((x + 9, y + 5), str(number), font=font, fill='white')

chart = Image.alpha_composite(chart, overlay).convert('RGB')
result = Image.new('RGB', (2423, 1927), '#172328')
result.paste(chart, (0, 0))
draw = ImageDraw.Draw(result)
title = ImageFont.truetype('arialbd.ttf', 27)
draw.text((1943, 28), 'ЗОНЫ PEI — ЧЕРНОВИК', font=title, fill='white')
draw.text((1943, 76), 'Красным: ускоренное гниение', font=font, fill='#ffd9ce')
draw.text((1943, 108), 'Границы не проверены в игре', font=font, fill='#ffd9ce')
for number, zone in enumerate(doc['zones'], 1):
    draw.text((1943, 160 + (number - 1) * 42), f'{number:02d}. {zone["name"]}', font=font, fill='white')
result.save(OUTPUT, optimize=True)
print(OUTPUT)
