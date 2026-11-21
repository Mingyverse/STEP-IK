namespace StepIK.Sandbox
{
    /// <summary>One sprint request lasts until movement input is released.</summary>
    public sealed class ManualSprintState
    {
        public bool Active { get; private set; }
        public bool WasMoving { get; private set; }

        public void Step(bool moving, bool requested, bool shiftHeld)
        {
            if (!moving) Active = false;
            else if (requested || (shiftHeld && !WasMoving)) Active = true;
            WasMoving = moving;
        }

        public void Reset()
        {
            Active = false;
            WasMoving = false;
        }
    }
}
