using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace TheKiwiCoder {

    // The context is a shared object every node has access to.
    // Commonly used components and subsytems should be stored here
    // It will be somewhat specfic to your game exactly what to add here.
    // Feel free to extend this class 
    public class Context {
        public GameObject gameObject;
        public Transform transform;
        public Animator animator;
        public bool animationCanChange;
        public Rigidbody physics;
        public NavMeshAgent agent;
        public SphereCollider sphereCollider;
        public BoxCollider boxCollider;
        public CapsuleCollider capsuleCollider;
        public CharacterController characterController;
        public float lastAttackTime;

        //添加自定义组件
        public ItemDetector itemDetector;
        public Mover_AI mover;
        public IItemValues itemValues;
        public Item item;
        public Map map;
        public DamageReceiver damageReciver;
        public Mod_ColdWeapon ColdWeapon;
        public Mod_Food Food;
        public TileEffectReceiver tileEffectReceiver;

        // Add other game specific systems here

        public static Context CreateFromGameObject(GameObject gameObject) {
            // Fetch all commonly used components
            Context context = new Context();
            context.item = gameObject.GetComponent<Item>();
            context.gameObject = gameObject;
            context.transform = context.item.transform;
            
            context.animator = context.item.GetComponentInChildren<Animator>();
            context.physics = context.item.GetComponent<Rigidbody>();
            context.agent = context.item.GetComponentInChildren<NavMeshAgent>();
            context.sphereCollider = context.item.GetComponent<SphereCollider>();
            context.boxCollider = context.item.GetComponent<BoxCollider>();
            context.capsuleCollider = context.item.GetComponent<CapsuleCollider>();
            context.characterController = context.item.GetComponent<CharacterController>();
            context.itemDetector = context.item.GetComponentInChildren<ItemDetector>();
            context.itemValues = context.item.GetComponentInChildren<IItemValues>();
            context.item = context.item.GetComponentInChildren<Item>();
            context.ColdWeapon = context.item.GetComponentInChildren<Mod_ColdWeapon>();
            // Add whatever else you need here...

            context.mover = context.item.itemMods.GetMod_ByID(ModText.Mover) as Mover_AI;
            context.Food = context.item.itemMods.GetMod_ByID(ModText.Food) as Mod_Food;
            context.damageReciver = context.item.itemMods.GetMod_ByID(ModText.Hp) as DamageReceiver;
            context.tileEffectReceiver = context.item.itemMods.GetMod_ByID(ModText.TileEffect) as TileEffectReceiver;
            context.map = context.tileEffectReceiver.Cache_map;
            return context;
        }
    }
}