using Satisfying.Shared;

namespace Satisfying.Tests
{
    /// <summary>
    /// Finger bookkeeping, tested here rather than discovered on a phone. Nearly every touch-control
    /// bug is an ownership bug: a thumb sliding off the stick starts turning the camera, a second
    /// finger on the trigger yanks the aim, a button lost because the finger drifted.
    /// </summary>
    public static class TouchRigTests
    {
        static TouchRig Rig()
        {
            TouchRig rig = new TouchRig();
            rig.Layout(1600f, 720f);     // a typical phone held sideways
            return rig;
        }

        public static void Register()
        {
            TestRunner.Add("touch/the stick appears where the thumb lands", () =>
            {
                TouchRig rig = Rig();
                rig.Begin(0, 200f, 200f);
                Assert.True(rig.StickActive, "the stick took the finger");
                Assert.Near(rig.StickOriginX, 200f, 0.01f, "and centred itself there");
                Assert.Near(rig.MoveX, 0f, 0.001f, "with no movement until it is pushed");

                rig.Move(0, 200f, 300f);
                Assert.Greater(rig.MoveY, 0.3f, "pushing up walks forward");
                Assert.Near(rig.MoveX, 0f, 0.05f, "and not sideways");

                rig.End(0);
                Assert.False(rig.StickActive, "lifting releases it");
                Assert.Near(rig.MoveY, 0f, 0.001f, "and stops the player");
            });

            TestRunner.Add("touch/a small wobble inside the dead zone is not movement", () =>
            {
                TouchRig rig = Rig();
                rig.Begin(0, 200f, 200f);
                rig.Move(0, 205f, 203f);
                Assert.Near(rig.MoveX, 0f, 0.001f, "still still");
                Assert.Near(rig.MoveY, 0f, 0.001f, "still still");
            });

            TestRunner.Add("touch/sprint comes from pushing the stick to its edge", () =>
            {
                TouchRig rig = Rig();
                rig.Begin(0, 200f, 200f);
                rig.Move(0, 200f, 260f);
                Assert.False(rig.Sprint, "a half push is a walk");

                rig.Move(0, 200f, 900f);
                Assert.True(rig.Sprint, "all the way is a run");
                Assert.Near(rig.MoveY, 1f, 0.001f, "at full stick");
            });

            TestRunner.Add("touch/the right thumb turns the camera and the left one does not", () =>
            {
                TouchRig rig = Rig();
                rig.Begin(0, 200f, 200f);          // left: stick
                rig.Begin(1, 1000f, 400f);         // right: look

                rig.Move(0, 260f, 200f);
                float x, y;
                rig.ConsumeLook(out x, out y);
                Assert.Near(x, 0f, 0.001f, "the stick hand never turns the view");

                rig.Move(1, 1080f, 400f);
                rig.ConsumeLook(out x, out y);
                Assert.Near(x, 80f, 0.001f, "the look hand does");
                rig.ConsumeLook(out x, out y);
                Assert.Near(x, 0f, 0.001f, "and the delta is consumed once");
            });

            TestRunner.Add("touch/a finger keeps its job for as long as it is down", () =>
            {
                // Slide the stick thumb across the middle of the screen and into the look region.
                TouchRig rig = Rig();
                rig.Begin(0, 200f, 200f);
                rig.Move(0, 1200f, 200f);

                float x, y;
                rig.ConsumeLook(out x, out y);
                Assert.Near(x, 0f, 0.001f, "still the stick, not the camera");
                Assert.Greater(rig.MoveX, 0.9f, "and still driving movement");
            });

            TestRunner.Add("touch/buttons win over the look region they sit inside", () =>
            {
                TouchRig rig = Rig();
                TouchButton fire = rig.Buttons[(int)TouchAction.Fire];
                rig.Begin(0, fire.X, fire.Y);

                Assert.True(rig.Held(TouchAction.Fire), "the trigger went down");
                float x, y;
                rig.ConsumeLook(out x, out y);
                Assert.Near(x, 0f, 0.001f, "and the camera did not move");

                rig.End(0);
                Assert.False(rig.Held(TouchAction.Fire), "released");
            });

            TestRunner.Add("touch/a thumb drifting off a button keeps it held", () =>
            {
                TouchRig rig = Rig();
                TouchButton fire = rig.Buttons[(int)TouchAction.Fire];
                rig.Begin(0, fire.X, fire.Y);
                rig.Move(0, fire.X + fire.Radius * 3f, fire.Y);
                Assert.True(rig.Held(TouchAction.Fire), "still firing");
                rig.End(0);
                Assert.False(rig.Held(TouchAction.Fire), "until the finger lifts");
            });

            TestRunner.Add("touch/crouch latches so a thumb is not tied up holding it", () =>
            {
                TouchRig rig = Rig();
                TouchButton crouch = rig.Buttons[(int)TouchAction.Crouch];

                rig.Begin(0, crouch.X, crouch.Y);
                rig.End(0);
                Assert.True(rig.Held(TouchAction.Crouch), "still crouched after letting go");

                rig.Begin(1, crouch.X, crouch.Y);
                rig.End(1);
                Assert.False(rig.Held(TouchAction.Crouch), "and stands up on the second press");
            });

            TestRunner.Add("touch/losing focus puts everything down", () =>
            {
                TouchRig rig = Rig();
                rig.Begin(0, 200f, 200f);
                rig.Move(0, 200f, 900f);
                TouchButton fire = rig.Buttons[(int)TouchAction.Fire];
                rig.Begin(1, fire.X, fire.Y);

                rig.ReleaseAll();
                Assert.Near(rig.MoveY, 0f, 0.001f, "not still walking");
                Assert.False(rig.Held(TouchAction.Fire), "not still firing");
                Assert.False(rig.StickActive, "and the stick is gone");
            });

            TestRunner.Add("touch/the controls stay on screen at any shape", () =>
            {
                float[][] screens = {
                    new[] { 1600f, 720f }, new[] { 2400f, 1080f }, new[] { 720f, 1600f }, new[] { 2048f, 1536f }
                };
                for (int s = 0; s < screens.Length; s++)
                {
                    TouchRig rig = new TouchRig();
                    rig.Layout(screens[s][0], screens[s][1]);
                    for (int i = 0; i < rig.Buttons.Length; i++)
                    {
                        TouchButton b = rig.Buttons[i];
                        Assert.Greater(b.X - b.Radius, -1f, b.Label + " is not off the left at " + screens[s][0] + "x" + screens[s][1]);
                        Assert.Less(b.X + b.Radius, screens[s][0] + 1f, b.Label + " is not off the right");
                        Assert.Greater(b.Y - b.Radius, -1f, b.Label + " is not off the bottom");
                        Assert.Less(b.Y + b.Radius, screens[s][1] + 1f, b.Label + " is not off the top");
                    }
                }
            });
        }
    }
}
