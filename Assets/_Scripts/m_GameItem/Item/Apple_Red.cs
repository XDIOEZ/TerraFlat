using ColorThief;
using DG.Tweening;
using MemoryPack;
using NUnit.Framework.Interfaces;
using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;

public class Apple_Red : Item,IFood
{
    public Apple_Red_Data _data;
    public override ItemData Item_Data
    {
        get
        {
            return _data;
        }
        set
        {
            _data = (Apple_Red_Data)value;
        }
    }
    public Hunger_Water Foods { get => _data.Energy_food; set => _data.Energy_food = value; }
    public UltEvent OnNutrientChanged { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
    public float EatingValue = 0;
    public override void Act()
    {
    }

    public Hunger_Water BeEat(float eatSpeed)
    {
        if (Item_Data == null || Foods == null)  return null;

        // ʹ�� DOTween ����������
        ShakeItem();

        EatingValue += eatSpeed;
        if (EatingValue >= Foods.MaxFood)
        {
            Item_Data.Stack.Amount--;

            UpdatedUI_Event?.Invoke();

            Foods.Food = Foods.MaxFood;
            Foods.Water = Foods.MaxWater;
             
            EatingValue = 0;

            if (Item_Data.Stack.Amount <= 0)
            {
                //ֹͣDoTween����
                transform.DOKill();
                Destroy(gameObject);
            }
            return Foods;
        }
        return null;
    }
    [Button("����")]
    private void ShakeItem(float duration = 0.2f, float strength = 0.2f, int vibrato = 0)
    {
        if (vibrato == 0)
        {
            //����һ������Ķ���ƫ����
            vibrato = Random.Range(15, 30);
        }
        // �� DOTween ���ֲ�����
        transform.DOShakePosition(duration, strength, vibrato).SetEase(Ease.OutQuad);

        // ���÷�װ������Ӵ�������
        CreateMainColorParticle(transform, "Particle_BeEat");
    }

    private GameObject CreateMainColorParticle(Transform targetTransform, string prefabName)
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

