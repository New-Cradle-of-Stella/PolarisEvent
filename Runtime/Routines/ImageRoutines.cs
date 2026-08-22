using System.Collections.Generic;

namespace Polaris.Pevt.Runtime.Routines
{
    /// <summary>
    /// 通用演出图层组合：<c>@image_*</c>、<c>@cg_show</c> 与 <c>@silhouette_show</c>。
    /// 坐标、透明度、帧数和 easing 都在产生副作用之前完成域验证。
    /// </summary>
    internal static class ImageRoutines
    {
        private static IPevtImage Image(PevtRoutineContext context) =>
            PevtArgumentDomains.RequireService(context.Services.Image, "Image");

        private static IPevtResources Resources(PevtRoutineContext context) =>
            PevtArgumentDomains.RequireService(context.Services.Resources, "Resources");

        public static IEnumerator<PevtWait> ImageShow(PevtRoutineContext context, PevtArguments args)
        {
            IPevtImage image = Image(context);
            string layerId = PevtArgumentDomains.RequireId(args.String(0), "layerId");
            string assetId = PevtArgumentDomains.RequireId(args.String(1), "assetId");

            yield return Resources(context).RequireImage(assetId);
            image.SetContent(layerId, assetId);
            image.SetVisible(layerId, true);
        }

        public static IEnumerator<PevtWait> ImageShowAt(PevtRoutineContext context, PevtArguments args)
        {
            IPevtImage image = Image(context);
            string layerId = PevtArgumentDomains.RequireId(args.String(0), "layerId");
            string assetId = PevtArgumentDomains.RequireId(args.String(1), "assetId");
            float x = PevtArgumentDomains.RequireFinite(args.Float(2), "x");
            float y = PevtArgumentDomains.RequireFinite(args.Float(3), "y");

            yield return Resources(context).RequireImage(assetId);
            image.SetContent(layerId, assetId);
            image.SetPosition(layerId, x, y);
            image.SetVisible(layerId, true);
        }

        public static IEnumerator<PevtWait> ImageHide(PevtRoutineContext context, PevtArguments args)
        {
            IPevtImage image = Image(context);
            string layerId = PevtArgumentDomains.RequireId(args.String(0), "layerId");
            int frames = PevtArgumentDomains.RequireFrames(args.Int(1), "frames");

            yield return image.FadeTo(layerId, 0f, frames, "linear");
            image.SetVisible(layerId, false);
        }

        public static IEnumerator<PevtWait> ImageClear(PevtRoutineContext context, PevtArguments args)
        {
            IPevtImage image = Image(context);
            string groupId = PevtArgumentDomains.RequireId(args.String(0), "groupId");
            int frames = PevtArgumentDomains.RequireFrames(args.Int(1), "frames");

            yield return image.ClearGroup(groupId, frames);
        }

        public static IEnumerator<PevtWait> ImageMove(PevtRoutineContext context, PevtArguments args)
        {
            IPevtImage image = Image(context);
            string layerId = PevtArgumentDomains.RequireId(args.String(0), "layerId");
            float x = PevtArgumentDomains.RequireFinite(args.Float(1), "x");
            float y = PevtArgumentDomains.RequireFinite(args.Float(2), "y");
            int frames = PevtArgumentDomains.RequireFrames(args.Int(3), "frames");
            string easing = PevtArgumentDomains.RequireEasing(args.String(4));

            yield return image.MoveTo(layerId, x, y, frames, easing);
        }

        public static IEnumerator<PevtWait> ImageMoveBy(PevtRoutineContext context, PevtArguments args)
        {
            IPevtImage image = Image(context);
            string layerId = PevtArgumentDomains.RequireId(args.String(0), "layerId");
            float x = PevtArgumentDomains.RequireFinite(args.Float(1), "x");
            float y = PevtArgumentDomains.RequireFinite(args.Float(2), "y");
            int frames = PevtArgumentDomains.RequireFrames(args.Int(3), "frames");
            string easing = PevtArgumentDomains.RequireEasing(args.String(4));

            yield return image.MoveBy(layerId, x, y, frames, easing);
        }

        public static IEnumerator<PevtWait> ImageFade(PevtRoutineContext context, PevtArguments args)
        {
            IPevtImage image = Image(context);
            string layerId = PevtArgumentDomains.RequireId(args.String(0), "layerId");
            float opacity = PevtArgumentDomains.RequireUnitRange(args.Float(1), "opacity");
            int frames = PevtArgumentDomains.RequireFrames(args.Int(2), "frames");
            string easing = PevtArgumentDomains.RequireEasing(args.String(3));

            yield return image.FadeTo(layerId, opacity, frames, easing);
        }

        public static IEnumerator<PevtWait> ImageScale(PevtRoutineContext context, PevtArguments args)
        {
            IPevtImage image = Image(context);
            string layerId = PevtArgumentDomains.RequireId(args.String(0), "layerId");
            float x = PevtArgumentDomains.RequireFinite(args.Float(1), "x");
            float y = PevtArgumentDomains.RequireFinite(args.Float(2), "y");
            int frames = PevtArgumentDomains.RequireFrames(args.Int(3), "frames");
            string easing = PevtArgumentDomains.RequireEasing(args.String(4));

            yield return image.ScaleTo(layerId, x, y, frames, easing);
        }

        public static IEnumerator<PevtWait> ImageRotate(PevtRoutineContext context, PevtArguments args)
        {
            IPevtImage image = Image(context);
            string layerId = PevtArgumentDomains.RequireId(args.String(0), "layerId");
            float degrees = PevtArgumentDomains.RequireFinite(args.Float(1), "degrees");
            int frames = PevtArgumentDomains.RequireFrames(args.Int(2), "frames");
            string easing = PevtArgumentDomains.RequireEasing(args.String(3));

            yield return image.RotateTo(layerId, degrees, frames, easing);
        }

        public static IEnumerator<PevtWait> ImageTint(PevtRoutineContext context, PevtArguments args)
        {
            IPevtImage image = Image(context);
            string layerId = PevtArgumentDomains.RequireId(args.String(0), "layerId");
            string color = PevtArgumentDomains.RequireColor(args.String(1));
            int frames = PevtArgumentDomains.RequireFrames(args.Int(2), "frames");

            yield return image.TintTo(layerId, color, frames);
        }

        public static IEnumerator<PevtWait> ImageFlip(PevtRoutineContext context, PevtArguments args)
        {
            Image(context).SetFlip(PevtArgumentDomains.RequireId(args.String(0), "layerId"), args.Bool(1), args.Bool(2));
            yield break;
        }

        public static IEnumerator<PevtWait> ImageOrder(PevtRoutineContext context, PevtArguments args)
        {
            Image(context).SetOrder(PevtArgumentDomains.RequireId(args.String(0), "layerId"), args.Int(1));
            yield break;
        }

        public static IEnumerator<PevtWait> ImageFill(PevtRoutineContext context, PevtArguments args)
        {
            IPevtImage image = Image(context);
            string layerId = PevtArgumentDomains.RequireId(args.String(0), "layerId");
            string color = PevtArgumentDomains.RequireColor(args.String(1));

            image.Fill(layerId, color);
            yield break;
        }

        /// <summary>`Resources.RequireImage` → `OpenSingle` → `WaitSingleClose` → `CloseSingle`。</summary>
        public static IEnumerator<PevtWait> CgShow(PevtRoutineContext context, PevtArguments args)
        {
            IPevtImage image = Image(context);
            string assetId = PevtArgumentDomains.RequireId(args.String(0), "assetId");

            yield return Resources(context).RequireImage(assetId);

            image.OpenSingle(assetId, args.String(1));
            context.Cleanup.Push("CloseSingle", image.CloseSingle);

            yield return image.WaitSingleClose();

            context.Cleanup.Pop();
            image.CloseSingle();
        }

        public static IEnumerator<PevtWait> SilhouetteShow(PevtRoutineContext context, PevtArguments args)
        {
            IPevtImage image = Image(context);
            string layerId = PevtArgumentDomains.RequireId(args.String(0), "layerId");
            string assetId = PevtArgumentDomains.RequireId(args.String(1), "assetId");
            string anchorId = PevtArgumentDomains.RequireId(args.String(2), "position");
            int frames = PevtArgumentDomains.RequireFrames(args.Int(3), "frames");

            yield return Resources(context).RequireImage(assetId);
            yield return image.ShowSilhouette(layerId, assetId, anchorId, frames);
        }
    }
}
