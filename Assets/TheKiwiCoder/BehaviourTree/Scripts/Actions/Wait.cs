using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TheKiwiCoder {
    public class Wait : ActionNode 
    {
        public float duration = 1;
        public float startTime;
/*        public float currentDuration;
        public float currentTime;*/

        protected override void OnStart() {
            startTime = Time.time;
        }

        protected override void OnStop() {
        }

        protected override State OnUpdate() {
            if (Time.time - startTime >= duration) {
                return State.Success;
            }
            Debug.Log("Wait");
         /*   currentDuration = Time.time - startTime;
            currentTime = Time.time;*/
            return State.Running;
        }
    }
}
