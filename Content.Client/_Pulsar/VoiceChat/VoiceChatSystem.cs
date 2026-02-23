using Content.Shared._Pulsar.CCVar;
using Content.Shared.Input;
using Content.Shared._Pulsar.VoiceChat;
using OpenTK.Audio.OpenAL;
using Robust.Client.Audio;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.Input.Binding;
using Robust.Shared.Utility;
using System.IO;
using Robust.Shared.Input;
using Robust.Shared.Player;
using Robust.Shared.Map;

namespace Content.Client._Pulsar.VoiceChat;

public sealed class VoiceChatSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IResourceManager _res = default!;
    [Dependency] private readonly AudioSystem _audio = default!;

    private static readonly MemoryContentRoot ContentRoot = new();
    private static readonly ResPath Prefix = ResPath.Root / "VoiceChat";
    private static bool _contentRootAdded;

    private bool _pushToTalk;
    private float _voiceVolume = 1f;
    private int _chunkId;

    public override void Initialize()
    {
        UpdatesOutsidePrediction = true;

        if (!_contentRootAdded)
        {
            _contentRootAdded = true;
            _res.AddRoot(Prefix, ContentRoot);
        }

        SubscribeNetworkEvent<VoiceChatAudioChunkEvent>(OnAudioChunk);

        CommandBinds.Builder
            .Bind(ContentKeyFunctions.PushToTalk, new PointerStateInputCmdHandler(HandlePushToTalkDown, HandlePushToTalkUp, outsidePrediction: true))
            .Register<VoiceChatSystem>();

        Subs.CVar(_cfg, CCVars.VoiceChatVolume, volume => _voiceVolume = volume, true);
    }

    public override void Shutdown()
    {
        CommandBinds.Unregister<VoiceChatSystem>();
        base.Shutdown();
    }

    public override void Update(float frameTime)
    {
        // Voice capture via OpenAL is blocked by Robust content sandbox.
        // Keep Push-To-Talk input state for future engine-level capture integration.
        if (!_pushToTalk || !_cfg.GetCVar(CCVars.VoiceChatClientEnabled) || !_cfg.GetCVar(CCVars.VoiceChatEnabled))
            return;
    }

    private bool HandlePushToTalkDown(ICommonSession? session, EntityCoordinates coordinates, EntityUid uid)
    {
        _pushToTalk = true;
        return false;
    }

    private bool HandlePushToTalkUp(ICommonSession? session, EntityCoordinates coordinates, EntityUid uid)
    {
        _pushToTalk = false;
        return false;
    }

    private void OnAudioChunk(VoiceChatAudioChunkEvent ev)
    {
        if (!_cfg.GetCVar(CCVars.VoiceChatClientEnabled))
            return;

        if (!TryGetEntity(ev.Speaker, out var speaker) || !(speaker?.Valid ?? false) || Deleted(speaker))
            return;

        var samples = BytesToPcm16(ev.Data);

        var chunkPath = new ResPath($"{_chunkId++}.wav");
        ContentRoot.AddOrUpdateFile(chunkPath, BuildWav(samples, ev.SampleRate));

        var resource = new AudioResource();
        resource.Load(IoCManager.Instance!, Prefix / chunkPath);

        var soundSpecifier = new ResolvedPathSpecifier(Prefix / chunkPath);
        var audioParams = AudioParams.Default.WithVolume(SharedAudioSystem.GainToVolume(_voiceVolume));

        _audio.PlayEntity(resource.AudioStream, speaker.Value, soundSpecifier, audioParams);
        ContentRoot.RemoveFile(chunkPath);
    }

    private static short[] BytesToPcm16(byte[] bytes)
    {
        var samples = new short[bytes.Length / 2];

        for (var i = 0; i < samples.Length; i++)
        {
            var lo = bytes[i * 2];
            var hi = bytes[i * 2 + 1];
            samples[i] = (short)(lo | (hi << 8));
        }

        return samples;
    }

    private static byte[] BuildWav(short[] samples, int sampleRate)
    {
        const short channels = 1;
        const short bitsPerSample = 16;

        var dataLength = samples.Length * 2;
        var byteRate = sampleRate * channels * bitsPerSample / 8;
        var blockAlign = channels * bitsPerSample / 8;

        var wav = new byte[44 + dataLength];

        wav[0] = (byte)'R'; wav[1] = (byte)'I'; wav[2] = (byte)'F'; wav[3] = (byte)'F';
        WriteInt32LE(wav, 4, 36 + dataLength);
        wav[8] = (byte)'W'; wav[9] = (byte)'A'; wav[10] = (byte)'V'; wav[11] = (byte)'E';
        wav[12] = (byte)'f'; wav[13] = (byte)'m'; wav[14] = (byte)'t'; wav[15] = (byte)' ';
        WriteInt32LE(wav, 16, 16);
        WriteInt16LE(wav, 20, 1);
        WriteInt16LE(wav, 22, channels);
        WriteInt32LE(wav, 24, sampleRate);
        WriteInt32LE(wav, 28, byteRate);
        WriteInt16LE(wav, 32, blockAlign);
        WriteInt16LE(wav, 34, bitsPerSample);
        wav[36] = (byte)'d'; wav[37] = (byte)'a'; wav[38] = (byte)'t'; wav[39] = (byte)'a';
        WriteInt32LE(wav, 40, dataLength);

        for (var i = 0; i < samples.Length; i++)
        {
            var sample = samples[i];
            wav[44 + i * 2] = (byte)(sample & 0xFF);
            wav[44 + i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
        }

        return wav;
    }

    private static void WriteInt16LE(byte[] data, int offset, int value)
    {
        data[offset] = (byte)(value & 0xFF);
        data[offset + 1] = (byte)((value >> 8) & 0xFF);
    }

    private static void WriteInt32LE(byte[] data, int offset, int value)
    {
        data[offset] = (byte)(value & 0xFF);
        data[offset + 1] = (byte)((value >> 8) & 0xFF);
        data[offset + 2] = (byte)((value >> 16) & 0xFF);
        data[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

}
