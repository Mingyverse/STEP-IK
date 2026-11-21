using UnityEngine;

namespace StepIK.Sandbox
{
    public sealed class CrowdProfile : MonoBehaviour
    {
        public float walkSeconds, idleSeconds;
        public AnimationCurve leftStance, rightStance;
        public float runSeconds;
        public AnimationCurve leftRunStance, rightRunStance;
    }
}
