using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    public static readonly CVarDef<bool> VoiceChatEnabled =
        CVarDef.Create("voicechat.enabled", false, CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<bool> VoiceChatClientEnabled =
        CVarDef.Create("voicechat.client_enabled", true, CVar.ARCHIVE | CVar.CLIENTONLY);

    public static readonly CVarDef<float> VoiceChatVolume =
        CVarDef.Create("voicechat.volume", 1.0f, CVar.ARCHIVE | CVar.CLIENTONLY);
}
