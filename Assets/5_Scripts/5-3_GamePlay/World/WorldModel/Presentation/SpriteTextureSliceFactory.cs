using UnityEngine;

public static class SpriteTextureSliceFactory
{
    #region 原图像素切片与图集坐标转换
    // 切片坐标和 PPU 同步换算，打包后仍保留原图的世界尺寸。
    public static Sprite CreateSourcePixelRectangleSprite(Sprite source, Rect sourceRectangle, Vector2 pivot,
        float pixelsPerUnit, SpriteMeshType meshType = SpriteMeshType.FullRect)
    {
        Rect original = source.rect, texture = source.textureRect;
        Vector2 ratio = new(texture.width / original.width, texture.height / original.height);
        Rect rectangle = new(texture.x + (sourceRectangle.x - original.x) * ratio.x,
            texture.y + (sourceRectangle.y - original.y) * ratio.y,
            sourceRectangle.width * ratio.x, sourceRectangle.height * ratio.y);
        return Sprite.Create(source.texture, rectangle, pivot, pixelsPerUnit * ratio.y, 0, meshType);
    }
    #endregion
}
