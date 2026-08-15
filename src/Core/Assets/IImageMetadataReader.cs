// SPDX-License-Identifier: GPL-3.0-or-later
namespace Core.Assets;

public sealed record ImageMetadata(int Width, int Height, int FrameCount);

/// <summary>Decodes image headers; implemented in Infrastructure (§6.3 abstraction rule).</summary>
public interface IImageMetadataReader
{
    /// <summary>Returns null when the content cannot be decoded as an image.</summary>
    ImageMetadata? TryRead(byte[] content);
}
