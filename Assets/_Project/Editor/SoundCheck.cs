using System.Text;
using UnityEditor;
using UnityEngine;
using Satisfying.Shared;

namespace Satisfying.Game
{
    /// <summary>
    /// Puts the sound propagation model in front of a set of shapes and prints what it decides, so
    /// "you can hear him through the wall" and "it comes from the corner" are numbers rather than
    /// opinions. There is no way to listen to a headless run, and the audio is exactly the kind of
    /// thing that is easy to get confidently wrong in silence.
    ///
    /// Each row is one situation: a source, a listener, and something between them.
    /// </summary>
    public static class SoundCheck
    {
        const float Rifle = 210f;       // WeaponTuning.soundCarry for the M4
        const float Boot = 25f;

        [MenuItem("Satisfying/Shots/Check the sound paths", priority = 63)]
        public static void Run()
        {
            GameObject stage = new GameObject("sound stage");
            Transform ear = new GameObject("ear").transform;
            ear.SetParent(stage.transform, false);
            ear.position = new Vector3(0f, 1.6f, 0f);

            SoundPropagation model = new SoundPropagation();
            model.Feel = new FeelTuning();
            model.Listener = ear;
            model.Mask = 1 << GameBootstrap.LayerWorld;

            StringBuilder report = new StringBuilder();
            report.AppendLine("[sound] ear at the origin at head height, source 8 m down +Z.");
            report.AppendLine("        'heard' is the level after the rolloff, so the open-ground rows are the baseline.");
            report.AppendLine("  situation                          carry    heard   cutoff   thick  arrives from");

            Vector3 head = new Vector3(0f, 1.6f, 8f);
            Vector3 foot = new Vector3(0f, 0.2f, 8f);

            Row(report, model, "open ground, rifle", head, Rifle);
            Row(report, model, "open ground, footstep", foot, Boot);

            // A crate in the middle of an open room. The old model silenced this completely, and a man
            // walking behind a box vanishing is the whole complaint.
            Box(stage, new Vector3(0f, 0.6f, 4f), new Vector3(1.4f, 1.2f, 1.4f));
            Row(report, model, "footstep behind a crate", foot, Boot);
            Row(report, model, "rifle behind a crate", new Vector3(0f, 0.9f, 8f), Rifle);
            Clear(stage);

            // A pillar: narrow, so the way round it is barely a detour and it should hardly matter.
            Box(stage, new Vector3(0f, 2f, 4f), new Vector3(0.6f, 4f, 0.6f));
            Row(report, model, "footstep behind a pillar", foot, Boot);
            Clear(stage);

            // Sealed rooms. Nothing bends round these, so the only way in is through the material -
            // which is where "loud enough still gets through" has to earn itself.
            Sealed(stage, 0.2f);
            Row(report, model, "sealed room, 0.2 m wall, rifle", head, Rifle);
            Row(report, model, "sealed room, 0.2 m wall, step", foot, Boot);
            Clear(stage);

            Sealed(stage, 0.6f);
            Row(report, model, "sealed room, 0.6 m wall, rifle", head, Rifle);
            Row(report, model, "sealed room, 0.6 m wall, step", foot, Boot);
            Clear(stage);

            Sealed(stage, 1.5f);
            Row(report, model, "sealed room, 1.5 m wall, rifle", head, Rifle);
            Row(report, model, "sealed room, 1.5 m wall, step", foot, Boot);
            Clear(stage);

            // The one that matters most: a wall with a doorway off to one side. The sound has to
            // arrive from the doorway, not from straight ahead through the wall.
            SealedWithDoorway(stage, 0.3f);
            Row(report, model, "room with a doorway, step", foot, Boot);
            Row(report, model, "room with a doorway, rifle", head, Rifle);
            Clear(stage);

            Object.DestroyImmediate(stage);
            Debug.Log(report.ToString());
        }

        static void Box(GameObject stage, Vector3 centre, Vector3 size)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "blocker";
            box.layer = GameBootstrap.LayerWorld;
            box.transform.SetParent(stage.transform, false);
            box.transform.position = centre;
            box.transform.localScale = size;
            Physics.SyncTransforms();
        }

        /// <summary>
        /// A box round the listener with the given wall thickness, and a lid on it.
        ///
        /// The walls OVERLAP at the corners. Butting them up flush leaves a notch the thickness of a
        /// wall at each corner, and the propagation model - correctly - finds its way out through the
        /// notch, which makes a sealed room read as a room with four small holes in it. The first
        /// version of this check had exactly that bug and blamed the model for it.
        /// </summary>
        static void Sealed(GameObject stage, float thickness)
        {
            const float r = 4f;
            const float h = 4f;
            float span = r * 2f + thickness * 2f;
            Box(stage, new Vector3(0f, h * 0.5f, r), new Vector3(span, h, thickness));
            Box(stage, new Vector3(0f, h * 0.5f, -r), new Vector3(span, h, thickness));
            Box(stage, new Vector3(r, h * 0.5f, 0f), new Vector3(thickness, h, span));
            Box(stage, new Vector3(-r, h * 0.5f, 0f), new Vector3(thickness, h, span));
            Box(stage, new Vector3(0f, h, 0f), new Vector3(span, thickness, span));
            Box(stage, new Vector3(0f, 0f, 0f), new Vector3(span, thickness, span));
        }

        /// <summary>
        /// The same room, but the far wall has a doorway in it - off to one side, so the straight line
        /// to the source is still solid wall and the only way in is round the jamb.
        /// </summary>
        static void SealedWithDoorway(GameObject stage, float thickness)
        {
            const float r = 4f;
            const float h = 4f;
            float span = r * 2f + thickness * 2f;
            Box(stage, new Vector3(0f, h * 0.5f, -r), new Vector3(span, h, thickness));
            Box(stage, new Vector3(r, h * 0.5f, 0f), new Vector3(thickness, h, span));
            Box(stage, new Vector3(-r, h * 0.5f, 0f), new Vector3(thickness, h, span));
            Box(stage, new Vector3(0f, h, 0f), new Vector3(span, thickness, span));
            Box(stage, new Vector3(0f, 0f, 0f), new Vector3(span, thickness, span));
            // Solid from x -4.4 to +1.4, a metre of doorway, then solid again to the corner.
            Box(stage, new Vector3(-1.5f, h * 0.5f, r), new Vector3(5.8f, h, thickness));
            Box(stage, new Vector3(3.45f, h * 0.5f, r), new Vector3(1.9f, h, thickness));
        }

        static void Clear(GameObject stage)
        {
            for (int i = stage.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = stage.transform.GetChild(i);
                if (child.name == "blocker") Object.DestroyImmediate(child.gameObject);
            }
            Physics.SyncTransforms();
        }

        static void Row(StringBuilder report, SoundPropagation model, string label, Vector3 source, float carry)
        {
            SoundPath path = model.Solve(source, carry);

            Vector3 ear = model.Listener.position;
            float heard = path.Gain * Rolloff(Vector3.Distance(ear, path.Position));

            Vector3 trueDir = (source - ear).normalized;
            Vector3 heardDir = (path.Position - ear).normalized;
            float off = Vector3.Angle(trueDir, heardDir);

            string arrival;
            if (path.Bent) arrival = "round a corner, " + off.ToString("0") + " deg off";
            else if (path.Thickness > 0.0001f) arrival = "through the wall";
            else arrival = "clear line";

            report.AppendLine("  " + label.PadRight(33) +
                              carry.ToString("0").PadLeft(6) +
                              heard.ToString("0.000").PadLeft(9) +
                              path.Cutoff.ToString("0").PadLeft(9) +
                              path.Thickness.ToString("0.00").PadLeft(8) + "  " + arrival);
        }

        static float Rolloff(float distance)
        {
            if (distance <= 3f) return 1f;
            if (distance >= 70f) return 0f;
            return 1f - (distance - 3f) / 67f;
        }
    }
}
