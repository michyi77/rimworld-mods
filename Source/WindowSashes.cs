using System;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace ProjectZ.Windows
{
    // The window's frame and its two sashes, drawn every frame from their own models so a colonist's work slides the
    // leaves over the opening - and so the whole window can be turned when the wall it sits in is drawn diagonally.
    //
    // The frame used to be static and baked by IsoRim's model definition (IsoRim/Defs/PZ_WindowModels.xml); it is drawn
    // here now, because Vanilla Diagonal Walls trims the wall into a band and a baked frame cannot be turned per cell
    // (SPEC §39). The drawing follows IsoRim's own door leaves (Dynamic.DrawOne): the same sash model twice, the second
    // turned 180 degrees, each moved out along the wall.
    //
    // The same code draws the ghost of a planned window: IsoRim keeps blueprints and frames flat on the floor
    // (Baking.cs:119), where the half wall hides them, so WindowGhosts hands a blueprint or a frame its window model at
    // the window's own height instead.
    //
    // Everything that names an IsoRim type sits in the inner methods on purpose: if IsoRim is missing, only those fail
    // to load, and the public entry points have already returned before they are reached.
    public static class WindowSashes
    {
        // How far each leaf slides when the window is fully open, in cells. The frame's inner edge is at 0.438 and a
        // wall is one cell deep, so 0.47 tucks the leaf into the wall beside it. (The model is back to its full 0.996
        // width: the 0.956 of 1.1.6 left a real gap between the window and the walls on either side - SPEC §29.)
        public const float Slide = 0.47f;

        // The frame model's own width, in cells (0.498 each side). The diagonal fit stretches the frame by
        // bandLength / FrameWidth so a diagonally trimmed window still meets the cells on either side.
        public const float FrameWidth = 0.996f;

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
                DrawParts(window, openPct, true, window.DrawColor, IsoRim.R3DTextures.StuffSwatch(window));
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
            // Square in the cell by default: the thing's own rotation, as the baked frame used to be.
            float yaw = t.Rotation.AsAngle;
            Vector3 centre = new Vector3(c.x + 0.5f, 0f, c.z + 0.5f);
            float width = 1f;
            // A diagonally trimmed wall (Vanilla Diagonal Walls): follow the band it was cut into, so the frame lies
            // along the diagonal and stretches to the cell's corners instead of standing square in the middle.
            DiagonalWindowFit.Fit fit;
            if (DiagonalWindowFit.TryFit(t.Map, c, out fit))
            {
                yaw = fit.yaw;
                centre += new Vector3(fit.offsetX, 0f, fit.offsetZ);
                width = Mathf.Clamp(fit.length / FrameWidth, 0.7f, 1.6f);
            }
            Vector3 along = Quaternion.AngleAxis(yaw, Vector3.up) * Vector3.right;
            float open = Mathf.Clamp01(openPct) * Slide * width;
            Matrix4x4 local = Mathf.Approximately(width, 1f)
                ? Matrix4x4.identity
                : Matrix4x4.Scale(new Vector3(width, 1f, 1f));
            if (withFrame && frameModel != null)
            {
                IsoRim.DynamicModels.DrawLocal(frameModel, centre, yaw, local, tint, swatch);
            }
            // The sash model is built at the left leaf's shut position: it moves out one way, the same model turned half
            // round moves out the other. The slides scale with the frame, or a wider window would not close.
            IsoRim.DynamicModels.DrawLocal(sashModel, centre - along * open, yaw, local, tint, swatch);
            IsoRim.DynamicModels.DrawLocal(sashModel, centre + along * open, yaw + 180f, local, tint, swatch);
        }
    }
}
