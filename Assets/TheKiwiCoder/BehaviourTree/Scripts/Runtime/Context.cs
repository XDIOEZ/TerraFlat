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
        public Rigidbody physics;
        public NavMeshAgent agent;
        public SphereCollider sphereCollider;
        public BoxCollider boxCollider;
        public CapsuleCollider capsuleCollider;
        public CharacterController characterController;
        //添加自定义组件
        public IDetector itemDetector;
        public Mover mover;
        public IItemValues itemValues;
        public Item item;
        public Map map;
        public DamageReceiver damageReciver;
        public Mod_ColdWeapon ColdWeapon;
        // Add other game specific systems here

        public static Context CreateFromGameObject(GameObject gameObject) {
            // Fetch all commonly used components
            Context context = new Context();
            context.item = gameObject.GetComponent<Item>();
            context.gameObject = gameObject;
            context.transform = gameObject.transform;
            context.map = GameItemManager.Instance.Map;
            context.animator = gameObject.GetComponentInChildren<Animator>();
            context.physics = gameObject.GetComponent<Rigidbody>();
            context.agent = gameObject.GetComponentInChildren<NavMeshAgent>();
            context.sphereCollider = gameObject.GetComponent<SphereCollider>();
            context.boxCollider = gameObject.GetComponent<BoxCollider>();
            context.capsuleCollider = gameObject.GetComponent<CapsuleCollider>();
            context.characterController = gameObject.GetComponent<CharacterController>();
            context.itemDetector = gameObject.GetComponentInChildren<IDetector>();
            context.mover = context.item.Mods[ModText.Mover] as Mover;
            context.itemValues = gameObject.GetComponentInChildren<IItemValues>();
            context.item = gameObject.GetComponentInChildren<Item>();
            context.damageReciver = context.item.Mods[ModText.Hp] as DamageReceiver;
                context.ColdWeapon = context.item.GetComponentInChildren<Mod_ColdWeapon>();
            // Add whatever else you need here...

            return context;
        }
    }
}