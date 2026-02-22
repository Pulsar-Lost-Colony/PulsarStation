using Robust.Shared.Serialization;

namespace Content.Shared._Pulsar.VoiceChat;

[Serializable, NetSerializable]
public sealed class VoiceChatAudioChunkEvent : EntityEventArgs
{
    public NetEntity Speaker;
    public byte[] Data;
    public int SampleRate;

    public VoiceChatAudioChunkEvent(NetEntity speaker, byte[] data, int sampleRate)
    {
        Speaker = speaker;
        Data = data;
        SampleRate = sampleRate;
    }
}
