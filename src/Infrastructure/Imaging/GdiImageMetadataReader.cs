// SPDX-License-Identifier: GPL-3.0-or-later
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Core.Assets;

namespace Infrastructure.Imaging;

/// <summary>Reads image dimensions and frame counts via GDI+ (§12.5, GIF frame reporting).</summary>
public sealed class GdiImageMetadataReader : IImageMetadataReader
{
    public ImageMetadata? TryRead(byte[] content)
    {
        try
        {
            using var stream = new MemoryStream(content);
            using var image = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: false);

            var frameCount = 1;
            try
            {
                if (image.FrameDimensionsList.Any(d => d == FrameDimension.Time.Guid))
                {
                    frameCount = image.GetFrameCount(FrameDimension.Time);
                }
            }
            catch (Exception ex) when (ex is ExternalException or ArgumentException)
            {
                // Frame enumeration failure degrades to a single frame.
            }

            return new ImageMetadata(image.Width, image.Height, frameCount);
        }
        catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException or ExternalException or IOException)
        {
            return null;
        }
    }
}
