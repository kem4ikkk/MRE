// MRE flight-only fork of ShimmysAdminTools FlightSession.
// Original Copyright (c) ShimmyMySherbet and contributors; changes (c) 2026 MRE contributors.
// SPDX-License-Identifier: GPL-3.0-only
using System;

namespace MreFlight
{
    public sealed class MovementValues
    {
        public float Gravity,Speed,Jump;
        public MovementValues Copy() {return new MovementValues {Gravity=Gravity,Speed=Speed,Jump=Jump};}
    }
    // FlightSession's gravity-driven flight is isolated from Unity for state/cleanup tests.
    public sealed class FlightState
    {
        private readonly MovementValues previous;
        public bool Active {get;private set;}
        public float HorizontalSpeed {get;private set;}
        public float VerticalSpeed {get;private set;}
        public FlightState(MovementValues previous,float horizontal=1,float vertical=1)
        {
            if(previous==null)throw new ArgumentNullException("previous");
            Validate(horizontal,vertical);this.previous=previous.Copy();HorizontalSpeed=horizontal;VerticalSpeed=vertical;Active=true;
        }
        private static bool Finite(float value) {return !Single.IsNaN(value)&&!Single.IsInfinity(value);}
        public static void Validate(float horizontal,float vertical)
        {
            if(!Finite(horizontal)||horizontal<0.1f||horizontal>10||!Finite(vertical)||vertical<0.1f||vertical>5)throw new ArgumentOutOfRangeException("speed","Скорость по горизонтали 0.1–10, по вертикали 0.1–5.");
        }
        public void SetSpeeds(float horizontal,float vertical) {Validate(horizontal,vertical);if(!Active)throw new InvalidOperationException("Полет выключен.");HorizontalSpeed=horizontal;VerticalSpeed=vertical;}
        public MovementValues Tick(bool jump,bool sprint,bool lookingDown,bool authorized,bool sameLiveSession)
        {
            if(!Active||!authorized||!sameLiveSession)return Stop();
            // Upstream controls: Jump reverses gravity, Sprint while looking down descends.
            // No plugin hotkeys are used: Plugin0/1 remain reserved for TPA.
            return new MovementValues {Gravity=jump?-VerticalSpeed:(sprint&&lookingDown?VerticalSpeed:0),Speed=HorizontalSpeed,Jump=previous.Jump};
        }
        public MovementValues Stop() {Active=false;return previous.Copy();}
    }
}
