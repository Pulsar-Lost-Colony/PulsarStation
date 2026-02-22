using Content.Shared._Pulsar.CCVar;
using Content.Shared._Pulsar.VoiceChat;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Player;

namespace Content.Server._Pulsar.VoiceChat;

public sealed class VoiceChatSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IPlayerManager _players = default!;

    public override void Initialize()
    {
        SubscribeNetworkEvent<VoiceChatAudioChunkEvent>(OnAudioChunk);
    }

    private void OnAudioChunk(VoiceChatAudioChunkEvent ev, EntitySessionEventArgs args)
    {
        if (!_cfg.GetCVar(CCVars.VoiceChatEnabled))
            return;

        var senderEntity = args.SenderSession.AttachedEntity;
        if (senderEntity == null)
            return;

        var senderMap = Transform(senderEntity.Value).MapID;
        var relayed = new VoiceChatAudioChunkEvent(GetNetEntity(senderEntity.Value), ev.Data, ev.SampleRate);

        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is not { Valid: true } attached)
                continue;

            if (Transform(attached).MapID != senderMap)
                continue;

            if (session == args.SenderSession)
                continue;

            RaiseNetworkEvent(relayed, session.Channel);
        }
    }
}
