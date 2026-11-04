"""Arrange native Unity captures for review; never modifies project artwork."""
from pathlib import Path
import yaml
from PIL import Image, ImageDraw, ImageFont

root = Path(__file__).resolve().parents[2]
output = root / 'Library/FlatWorldDeviceArtwork'
captures = output / 'Previews'
source = (root / 'Assets/Resources/UI/PhysicalDeviceArtworkCatalog.asset').read_text(encoding='utf-8-sig')
panels = yaml.safe_load(source[source.index('MonoBehaviour:'):])['MonoBehaviour']['Panels']
font = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 18)
families = {'Tools': range(5), 'Facilities': range(5, 10), 'Industry': [10, 11, 12, 14, 15], 'Equipment': [13, 16, 17]}

for family, indexes in families.items():
    indexes = list(indexes)
    board = Image.new('RGB', (1200, len(indexes) * 370), '#1d201f')
    draw = ImageDraw.Draw(board)
    for row, index in enumerate(indexes):
        item = panels[index]
        for column, state in enumerate(['cold', 'working']):
            path = captures / f"{item['PanelId']}_{item['DefaultAppearance']}_{state}_0.04_1920x1080.png"
            image = Image.open(path)
            image.thumbnail((600, 338))
            board.paste(image, (column * 600, row * 370))
        draw.text((12, row * 370 + 340), item['PanelId'] + '  静止／工作状态预览', font=font, fill='white')
    board.save(output / (family + '.png'))

frames = []
for index in range(16):
    time = f'{index * .03:.2f}'
    board = Image.new('RGB', (1080, 620), '#1d201f')
    draw = ImageDraw.Draw(board)
    for column, (panel, appearance, caption) in enumerate([
        ('UI_FireDrill', 'SparkMaker', '钻木取火'), ('UI_HandDrill', 'HandDrill', '手钻')]):
        image = Image.open(captures / f'{panel}_{appearance}_working_{time}_1280x720.png')
        image = image.crop((310, 80, 970, 680))
        image.thumbnail((540, 565))
        board.paste(image, (column * 540, 25))
        draw.text((column * 540 + 30, 8), caption, font=font, fill='white')
    draw.text((30, 592), 'Unity 正式 Prefab 状态预览', font=font, fill='white')
    frames.append(board)
frames[0].save(output / 'DrillMotion.gif', save_all=True, append_images=frames[1:], duration=60, loop=0)
print('Wrote four family comparisons and DrillMotion.gif')
