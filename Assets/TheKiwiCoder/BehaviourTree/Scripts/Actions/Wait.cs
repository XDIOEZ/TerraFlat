using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TheKiwiCoder {
    public class Wait : ActionNode 
    {
        public float duration = 1;
        public float startTime;
        public float intervalTime;
/*        public float currentDuration;
        public float currentTime;*/

        protected override void OnStart() {
            startTime = Time.time;
        }

        protected override void OnStop() {
        }

        protected override State OnUpdate() {
            intervalTime = Time.time - startTime;
            if (intervalTime >= duration) {
                return State.Success;
            }
            return State.Running;
        }
    }
}
