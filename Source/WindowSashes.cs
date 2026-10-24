using System;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace ProjectZ.Windows
{
    // The window's two sashes, drawn every frame from their own model so a colonist's work slides them over the opening.
    //
    // The frame is static and baked by IsoRim's model definition (IsoRim/Defs/PZ_WindowModels.xml); only these two
    // leaves move, which is why the window is drawerType RealtimeOnly. The drawing follows IsoRim's own door leaves
    // (Dynamic.DrawOne): the same model twice, the second turned 180 degrees, each moved out along the wall.
    //
    // The same code draws the ghost of a planned window: IsoRim keeps blueprints and frames flat on the floor
    // (Baking.cs:119), where the half wall hides them, so WindowGhosts hands a blueprint or a frame its window model at
    // the window's own height instead.
    //
    // Everything that names an IsoRim type sits in the inner methods on purpose: if IsoRim is missing, only those fail
    // to load, and the public entry points have already returned before they are reached.
    public static class WindowSashes
    {
        // How far each leaf slides when the window is fully open, in cells. The frame's inner edge is at 0.420 (the
        // model is 0.956 of a cell wide since 1.1.6) and a wall is one cell deep, so 0.451 tucks the leaf into the
        // wall beside it.
        public const float Slide = 0.451f;

        // Blueprint blue, for a window that is only planned or half built.
        public static readonly Color GhostColor = new Color(0.45f, 0.7f, 1f);

        private static bool isoRimChecked;
        private static bool isoRimPresent;

        // Without IsoRim there is nothing to draw through: the mod still works (the def has a plain 2D graphic), it
        // just shows no glazing.
        public static bool IsoRimPresent
        {
            get
            {
                if (!isoRimChecked)
                {
                    isoRimChecked = true;
                    isoRimPresent = AccessTools.TypeByName("IsoRim.Api") != null
                        && AccessTools.TypeByName("IsoRim.ModelLibrary") != null
                        && AccessTools.TypeByName("IsoRim.DynamicModels") != null;
                }
                return isoRimPresent;
            }
        }

        private static bool modelsTried;
        private static IsoRim.Model frameModel;
        private static IsoRim.Model sashModel;

        // The window itself: the frame is baked, so only the sashes are drawn. openPct: 0 shut, 1 fully apart.
        public static void Draw(Thing window, float openPct)
        {
            if (!IsoRimPresent || window == null || window.Map == null)
            {
                return;
            }
            try
            {
                DrawParts(window, openPct, false, window.DrawColor, IsoRim.R3DTextures.StuffSwatch(window));
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[ProjectZ] Drawing the window sashes failed: " + e, 0x5A17C0);
            }
        }

        private static void DrawParts(Thing t, float openPct, bool withFrame, Color tint, string swatch)
        {
            if (!modelsTried)
            {
                modelsTried = true;
                string root = WindowsMod.RootDir;
                if (!root.NullOrEmpty())
                {
                    frameModel = IsoRim.ModelLibrary.LoadFile(System.IO.Path.Combine(root, "Models", "pz_window_glass.obj"));
                    sashModel = IsoRim.ModelLibrary.LoadFile(System.IO.Path.Combine(root, "Models", "pz_window_sash.obj"));
                }
            }
            if (!IsoRim.Api.ViewReady || sashModel == null)
            {
                return;
            }
            IntVec3 c = t.Position;
            // The model's turn, as the baked frame's is: the thing's own rotation.
            float yaw = t.Rotation.AsAngle;
            Vector3 along = Quaternion.AngleAxis(yaw, Vector3.up) * Vector3.right;
            Vector3 centre = new Vector3(c.x + 0.5f, 0f, c.z + 0.5f);
            float open = Mathf.Clamp01(openPct) * Slide;
            if (withFrame && frameModel != null)
            {
                IsoRim.DynamicModels.DrawLocal(frameModel, centre, yaw, Matrix4x4.identity, tint, swatch);
            }
            // The sash model is built at the left leaf's shut position: it moves out one way, the same model turned half
            // round moves out the other.
            IsoRim.DynamicModels.DrawLocal(sashModel, centre - along * open, yaw, Matrix4x4.identity, tint, swatch);
            IsoRim.DynamicModels.DrawLocal(sashModel, centre + along * open, yaw + 180f, Matrix4x4.identity, tint, swatch);
        }
    }
}
