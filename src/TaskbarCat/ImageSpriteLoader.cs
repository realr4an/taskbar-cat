using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;

namespace TaskbarCat;

internal static class ImageSpriteLoader
{
    public static Bitmap[] LoadFixedGrid(string resourceName, int columns, int rows, params int[] selected)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)!;
        using var sheet = new Bitmap(stream);
        int cellW = sheet.Width / columns, cellH = sheet.Height / rows;
        const int canvasW = 84, canvasH = 80;
        const int renderedSize = 76;
        var result = new Bitmap[selected.Length];

        for (int i = 0; i < selected.Length; i++)
        {
            int index = selected[i];
            using var cell = sheet.Clone(new Rectangle((index % columns) * cellW, (index / columns) * cellH, cellW, cellH), PixelFormat.Format32bppArgb);
            result[i] = new Bitmap(canvasW, canvasH, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(result[i]);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(cell, new Rectangle((canvasW - renderedSize) / 2, canvasH - renderedSize, renderedSize, renderedSize));
        }
        return result;
    }

    public static Bitmap[] LoadGrid(string resourceName, int columns, int rows, params int[] selected)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)!;
        using var sheet = new Bitmap(stream);
        int cellW = sheet.Width / columns, cellH = sheet.Height / rows;
        var sourceCells = new Bitmap[selected.Length];
        var bounds = new Rectangle[selected.Length];
        int widest = 1, tallest = 1;

        for (int i = 0; i < selected.Length; i++)
        {
            int index = selected[i];
            sourceCells[i] = sheet.Clone(new Rectangle((index % columns) * cellW, (index / columns) * cellH, cellW, cellH), PixelFormat.Format32bppArgb);
            bounds[i] = HardenAlphaAndFindBounds(sourceCells[i]);
            widest = Math.Max(widest, bounds[i].Width);
            tallest = Math.Max(tallest, bounds[i].Height);
        }

        const int canvasW = 84, canvasH = 80;
        float scale = Math.Min((float)canvasW / widest, (float)canvasH / tallest);
        var result = new Bitmap[selected.Length];
        for (int i = 0; i < selected.Length; i++)
        {
            result[i] = new Bitmap(canvasW, canvasH, PixelFormat.Format32bppArgb);
            int w = Math.Max(1, (int)Math.Round(bounds[i].Width * scale));
            int h = Math.Max(1, (int)Math.Round(bounds[i].Height * scale));
            using var g = Graphics.FromImage(result[i]);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(sourceCells[i], new Rectangle((canvasW - w) / 2, canvasH - h, w, h), bounds[i], GraphicsUnit.Pixel);
            sourceCells[i].Dispose();
        }
        return result;
    }

    public static Bitmap[] Mirror(Bitmap[] source)
    {
        var result = new Bitmap[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            result[i] = (Bitmap)source[i].Clone();
            result[i].RotateFlip(RotateFlipType.RotateNoneFlipX);
        }
        return result;
    }

    public static Bitmap[] ScaleContent(Bitmap[] source, float factor)
    {
        for (int i = 0; i < source.Length; i++)
        {
            var bounds = FindBounds(source[i]);
            var resized = new Bitmap(source[i].Width, source[i].Height, PixelFormat.Format32bppArgb);
            int width = Math.Min(resized.Width, Math.Max(1, (int)Math.Round(bounds.Width * factor)));
            int height = Math.Min(resized.Height, Math.Max(1, (int)Math.Round(bounds.Height * factor)));
            int x = (resized.Width - width) / 2;
            int y = resized.Height - height;
            using (var g = Graphics.FromImage(resized))
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(source[i], new Rectangle(x, y, width, height), bounds, GraphicsUnit.Pixel);
            }
            source[i].Dispose();
            source[i] = resized;
        }
        return source;
    }

    public static Bitmap[] StretchContent(Bitmap[] source, float widthFactor, float heightFactor, int canvasWidth, int canvasHeight)
    {
        for (int i = 0; i < source.Length; i++)
        {
            var bounds = FindBounds(source[i]);
            var adjusted = new Bitmap(canvasWidth, canvasHeight, PixelFormat.Format32bppArgb);
            int width = Math.Min(canvasWidth, Math.Max(1, (int)Math.Round(bounds.Width * widthFactor)));
            int height = Math.Min(canvasHeight, Math.Max(1, (int)Math.Round(bounds.Height * heightFactor)));
            using (var g = Graphics.FromImage(adjusted))
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(source[i], new Rectangle((canvasWidth - width) / 2, canvasHeight - height, width, height), bounds, GraphicsUnit.Pixel);
            }
            source[i].Dispose();
            source[i] = adjusted;
        }
        return source;
    }

    private static Rectangle FindBounds(Bitmap image)
    {
        int minX = image.Width, minY = image.Height, maxX = -1, maxY = -1;
        for (int y = 0; y < image.Height; y++) for (int x = 0; x < image.Width; x++)
        {
            if (image.GetPixel(x, y).A == 0) continue;
            minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
        }
        return maxX < 0 ? new Rectangle(0, 0, 1, 1) : Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
    }

    private static Rectangle HardenAlphaAndFindBounds(Bitmap image)
    {
        int minX = image.Width, minY = image.Height, maxX = -1, maxY = -1;
        for (int y = 0; y < image.Height; y++) for (int x = 0; x < image.Width; x++)
        {
            var p = image.GetPixel(x, y);
            if (p.A < 128) image.SetPixel(x, y, Color.Transparent);
            else
            {
                image.SetPixel(x, y, Color.FromArgb(255, p.R, p.G, p.B));
                minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
            }
        }
        return maxX < 0 ? new Rectangle(0, 0, 1, 1) : Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
    }
}
