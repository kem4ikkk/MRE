from PIL import Image, ImageDraw, ImageFont
from pathlib import Path

out = Path(__file__).with_name('hud-concept.png')
im = Image.new('RGB', (1920, 1080), '#26352f')
d = ImageDraw.Draw(im, 'RGBA')
for y in range(1080):
    t = y / 1080
    d.line((0,y,1920,y), fill=(int(61-35*t), int(93-42*t), int(99-52*t),255))
d.polygon([(0,800),(300,615),(550,740),(870,585),(1300,730),(1660,570),(1920,700),(1920,1080),(0,1080)], fill=(30,58,38,255))
d.polygon([(0,940),(350,800),(720,950),(1120,760),(1500,890),(1920,780),(1920,1080),(0,1080)], fill=(24,46,30,255))
font='C:/Windows/Fonts/arial.ttf'; bold='C:/Windows/Fonts/arialbd.ttf'
def txt(x,y,s,size,color='#eef2e8',weight=False): d.text((x,y),s,font=ImageFont.truetype(bold if weight else font,size),fill=color)
txt(32,30,'MRE  /  ПРЕДВАРИТЕЛЬНЫЙ ВИД',17,'#dce6d3',True)
txt(32,55,'Пример значений · фон условный · игровой масштаб 1920 × 1080',13,'#a7b8a8')
d.ellipse((958,538,962,542),fill=(240,242,235,255))
back=(10,19,16,218); muted='#aab9b2'; lime='#b8d66b'; water='#79b9d0'
for i,(label,value) in enumerate([('УБИЙСТВА','41'),('K/D','2,05'),('СМЕРТИ','20'),('ЗОМБИ','368')]):
    x=28+i*93; d.rectangle((x,952,x+91,987),fill=back)
    txt(x+6,954,label,9,muted); txt(x+6,968,value,13,weight=True)
d.rectangle((28,995,398,1052),fill=back)
for i,(label,value,pct,color) in enumerate([('ЗДОРОВЬЕ','92',.92,lime),('ЕДА','76',.76,lime),('ВОДА','72',.72,water),('ЭНЕРГИЯ','85',.85,lime)]):
    x=37+i*90; txt(x,1001,label,9,muted); txt(x,1014,value,16,weight=True)
    d.rectangle((x,1042,x+79,1046),fill=(62,76,68,255)); d.rectangle((x,1042,x+int(79*pct),1046),fill=color)
x,y=1632,970; d.rectangle((x,y,x+260,y+82),fill=back); d.rectangle((x,y,x+260,y+2),fill=lime)
txt(x+12,y+5,'EAGLEFIRE',12,weight=True); d.line((x+12,y+25,x+248,y+25),fill=(55,65,62,255))
txt(x+12,y+29,'30',28,weight=True); txt(x+88,y+34,'/ 30',17,muted); txt(x+194,y+35,'100%',12,muted)
txt(x+12,y+65,'Ranger Magazine',10,muted); txt(x+170,y+65,'АВТО',10,lime)
im.save(out)
print(out.resolve())

# Альтернативный вариант: компактные горизонтальные полосы вместо четырёх колонок.
im2 = Image.new('RGB', (1920, 1080), '#26352f')
d2 = ImageDraw.Draw(im2, 'RGBA')
for y in range(1080):
    t = y / 1080
    d2.line((0,y,1920,y), fill=(int(61-35*t), int(93-42*t), int(99-52*t),255))
d2.polygon([(0,800),(300,615),(550,740),(870,585),(1300,730),(1660,570),(1920,700),(1920,1080),(0,1080)], fill=(30,58,38,255))
d2.polygon([(0,940),(350,800),(720,950),(1120,760),(1500,890),(1920,780),(1920,1080),(0,1080)], fill=(24,46,30,255))
original_draw = d
d = d2
font = "C:/Windows/Fonts/tahoma.ttf"; bold = "C:/Windows/Fonts/tahomabd.ttf"
txt(32,30,'MRE  /  ПРЕДВАРИТЕЛЬНЫЙ ВИД С ПОЛОСАМИ',17,'#dce6d3',True)
txt(32,55,'Пример значений · фон условный · игровой масштаб 1920 × 1080',13,'#a7b8a8')
d.ellipse((958,538,962,542),fill=(240,242,235,255))
for i,(label,value) in enumerate([('УБИЙСТВА','41'),('K/D','2,05'),('СМЕРТИ','20'),('ЗОМБИ','368')]):
    x=28+i*72; d.rounded_rectangle((x,884,x+69,916),radius=3,fill=back)
    txt(x+5,886,label,9,muted); txt(x+5,899,value,12,weight=True)
d.rounded_rectangle((28,923,313,1052),radius=4,fill=back)
for i,(label,value,pct,color) in enumerate([('ЗДОРОВЬЕ','92',.92,lime),('ЕДА','76',.76,lime),('ВОДА','72',.72,water),('ЭНЕРГИЯ','85',.85,lime)]):
    y=930+i*30; txt(37,y,label,9,muted,True); txt(281,y,value,12,weight=True)
    d.rounded_rectangle((37,y+16,303,y+20),radius=2,fill=(62,76,68,255))
    d.rounded_rectangle((37,y+16,37+int(266*pct),y+20),radius=2,fill=color)
x,y=1722,992; d.rectangle((x,y,x+170,y+60),fill=back); d.rectangle((x,y,x+2,y+60),fill=lime)
txt(x+9,y+4,'EAGLEFIRE',10,weight=True)
txt(x+9,y+17,'30',20,weight=True); txt(x+39,y+24,'/ 30',12,muted); txt(x+128,y+25,'100%',10,muted)
txt(x+9,y+46,'Ranger Magazine',9,muted); txt(x+135,y+46,'АВТО',9,lime)
out2=Path(__file__).with_name('hud-bars-minimal-concept.png')
im2.save(out2)
print(out2.resolve())
d = original_draw
