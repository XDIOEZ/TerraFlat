using UnityEngine;

namespace FlatWorld.Spaceflight
{
    public sealed class SpaceStarfield : MonoBehaviour
    {
        #region 黑色稀疏星空
        private readonly SpriteRenderer[] stars = new SpriteRenderer[80];
        private readonly Vector2[] positions = new Vector2[80];
        private Texture2D texture;
        private Sprite sprite;
        private Material material;
        private Camera activeCamera;
        private Color previousColor;
        private CameraClearFlags previousFlags;

        private void Awake()
        {
            texture = new Texture2D(3, 3, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var pixels = new Color[9];
            pixels[4] = Color.white; pixels[1] = pixels[3] = pixels[5] = pixels[7] = new Color(1f, 1f, 1f, 0.18f);
            texture.SetPixels(pixels); texture.Apply();
            sprite = Sprite.Create(texture, new Rect(0f, 0f, 3f, 3f), new Vector2(0.5f, 0.5f), 3f);
            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null) material = new Material(shader);
            var random = new System.Random(17413);
            for (int i = 0; i < stars.Length; i++)
            {
                var star = new GameObject("背景星点"); star.transform.SetParent(transform, false);
                SpriteRenderer renderer = star.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite; renderer.sortingOrder = short.MinValue;
                if (material != null) renderer.sharedMaterial = material;
                float brightness = 0.25f + (float)random.NextDouble() * 0.65f;
                renderer.color = new Color(brightness, brightness, brightness, 1f);
                stars[i] = renderer;
                positions[i] = new Vector2((float)random.NextDouble(), (float)random.NextDouble());
            }
        }

        private void LateUpdate()
        {
            Camera camera = Camera.main;
            if (camera == null) return;
            if (activeCamera != camera)
            {
                RestoreCamera(); activeCamera = camera;
                previousColor = camera.backgroundColor; previousFlags = camera.clearFlags;
                camera.backgroundColor = Color.black; camera.clearFlags = CameraClearFlags.SolidColor;
            }
            for (int i = 0; i < stars.Length; i++)
            {
                Vector3 world = camera.ViewportToWorldPoint(new Vector3(positions[i].x, positions[i].y, 10f));
                world.z = 1f; stars[i].transform.position = world;
                float size = camera.orthographic ? camera.orthographicSize * 0.004f : 0.05f;
                stars[i].transform.localScale = Vector3.one * size;
            }
        }

        private void OnDisable() => RestoreCamera();
        private void RestoreCamera()
        {
            if (activeCamera == null) return;
            activeCamera.backgroundColor = previousColor; activeCamera.clearFlags = previousFlags;
            activeCamera = null;
        }
        private void OnDestroy()
        {
            RestoreCamera();
            if (sprite != null) Destroy(sprite);
            if (texture != null) Destroy(texture);
            if (material != null) Destroy(material);
        }
        #endregion
    }
}
