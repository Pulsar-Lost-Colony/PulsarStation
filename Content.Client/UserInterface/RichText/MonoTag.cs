using System.Linq;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface.RichText;
using Robust.Shared.Utility;

namespace Content.Client.UserInterface.RichText;

/// <summary>
/// Sets the font to a monospaced variant
/// </summary>
public sealed class MonoTag : IMarkupTagHandler
{
    private const string MonoFontPath = "/EngineFonts/NotoSans/NotoSansMono-Regular.ttf";

    [Dependency] private readonly IResourceCache _resourceCache = default!;

    public string Name => "mono";

    /// <inheritdoc/>
    public void PushDrawContext(MarkupNode node, MarkupDrawingContext context)
    {
        var size = FontTag.GetSizeForFontTag(context.Font, node);
        var fontResource = _resourceCache.GetResource<FontResource>(MonoFontPath);
        var font = new VectorFont(fontResource, size);
        context.Font.Push(font);
    }

    /// <inheritdoc/>
    public void PopDrawContext(MarkupNode node, MarkupDrawingContext context)
    {
        context.Font.Pop();
    }
}
