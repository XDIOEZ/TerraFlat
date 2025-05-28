
using DG.Tweening;
using Sirenix.OdinInspector;
using UnityEngine;

public interface IFood
{

    Nutrition NutritionData { get; set; }

    Nutrition BeEat(float BeEatSpeed);

    public IFood SelfFood { get => this; }

    [Button("抖动")]
    void ShakeItem(Transform transform, float duration = 0.2f, float strength = 0.2f, int vibrato = 0)
    {
        if (vibrato == 0)
        {
            //产生一个随机的抖动偏移量
            vibrato = Random.Range(15, 30);
        }
        // 用 DOTween 做局部抖动
        transform.DOShakePosition(duration, strength, vibrato).SetEase(Ease.OutQuad);

        // 调用封装后的粒子创建方法
        CreateMainColorParticle(transform, "Particle_BeEat");
    }

    private GameObject CreateMainColorParticle(UnityEngine.Transform targetTransform, string prefabName)
    {
        SpriteRenderer sr = targetTransform.GetComponentInChildren<SpriteRenderer>();

        if (sr != null && sr.sprite != null)
        {
            var dominant = new ColorThief.ColorThief();
            UnityEngine.Color mainColor = dominant.GetColor(sr.sprite.texture).UnityColor;

            GameObject particle = GameRes.Instance.InstantiatePrefab(prefabName, targetTransform.position);
            ParticleSystem ps = particle.GetComponent<ParticleSystem>();
            if (ps != null)
            {
                var main = ps.main;
                main.startColor = mainColor;
            }

            return particle;
        }

        return null;
    }

}